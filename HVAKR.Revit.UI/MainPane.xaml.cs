using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using HVAKR.Api;
using HVAKR.Api.Models;
using HVAKR.Api.Updates;
using ExternalEvent = Autodesk.Revit.UI.ExternalEvent;

namespace HVAKR.Revit.UI;

public partial class MainPane : UserControl
{
    private readonly IExportBridge? _exportBridge;
    private readonly UpdateService _updateService = new();
    private readonly bool _canInstallUpdates;

    private HttpClient? _httpClient;
    private Client? _apiClient;
    private Dictionary<string, ProjectDetails> _projectsById = new();
    private ProjectDetails? _selectedProject;
    private bool _isLoggedIn;
    private bool _retryingFailedUpdate;
    private long _projectSelectionVersion;

    public MainPane() : this(new RevitExportBridge())
    {
    }

    private MainPane(IExportBridge? exportBridge)
    {
        InitializeComponent();
        _exportBridge = exportBridge;
        _canInstallUpdates = exportBridge is not null;
        UpdateLoggedInState();
        Loaded += MainPane_Loaded;
    }

    public static MainPane CreateForHarness() => new(exportBridge: null);

    private async void MainPane_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainPane_Loaded;
        if (ShowSavedUpdateStatus()) return;

        CheckForUpdatesButton.IsEnabled = false;
        try
        {
            var result = await _updateService.CheckAsync(force: false);
            if (result.Outcome == UpdateCheckOutcome.Ready || result.Outcome == UpdateCheckOutcome.Throttled)
                ShowReadyUpdate(result.State);
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private void UpdateLoggedInState()
    {
        if (_isLoggedIn)
        {
            LoginButton.Content = "Logout";
            LoginButton.IsEnabled = true;
            ApiKeyBox.IsEnabled = false;
            DetailsPanel.IsEnabled = true;
        }
        else
        {
            LoginButton.Content = "Login";
            LoginButton.IsEnabled = true;
            ApiKeyBox.IsEnabled = true;
            ApiKeyBox.Password = string.Empty;
            ProjectPicker.ItemsSource = null;
            AddressLabel.Text = string.Empty;
            DetailsPanel.IsEnabled = false;
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoggedIn)
        {
            Logout();
            return;
        }

        var apiKey = ApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("Please enter an API key.", "Login", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoginButton.IsEnabled = false;
        LoginButton.Content = "Loading…";

        try
        {
            _httpClient = new HttpClient();
            _apiClient = new Client(_httpClient, apiKey);

            var projects = await _apiClient.GetProjectsAsync();
            if (projects.Count == 0)
            {
                MessageBox.Show("No projects found for this account.", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Warning);
                Logout();
                return;
            }

            _projectsById = new Dictionary<string, ProjectDetails>();
            var items = projects
                .Select(p => new ProjectListItem(p.Id, p.Name ?? p.Number ?? p.Id))
                .ToList();

            ProjectPicker.ItemsSource = items;
            ProjectPicker.SelectedIndex = 0;

            _isLoggedIn = true;
            UpdateLoggedInState();
        }
        catch (Exception ex)
        {
            Logger.LogError("Login failed", ex);
            MessageBox.Show($"Login failed: {ex.Message}", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Error);
            Logout();
        }
    }

    private void Logout()
    {
        _projectSelectionVersion++;
        _isLoggedIn = false;
        _apiClient = null;
        _selectedProject = null;
        _projectsById = new Dictionary<string, ProjectDetails>();
        _httpClient?.Dispose();
        _httpClient = null;
        UpdateLoggedInState();
    }

    private async void ProjectPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectPicker.SelectedItem is not ProjectListItem item) return;
        if (_apiClient is null) return;

        var selectionVersion = ++_projectSelectionVersion;
        _selectedProject = null;
        AddressLabel.Text = string.Empty;
        ExportButton.IsEnabled = false;

        if (!_projectsById.TryGetValue(item.Id, out var project))
        {
            try
            {
                var expanded = await _apiClient.GetProjectDetailsAsync(item.Id, expand: true);
                if (expanded is null) return;

                project = expanded;
                _projectsById[item.Id] = expanded;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to expand project {item.Id}", ex);
                return;
            }
        }

        if (selectionVersion != _projectSelectionVersion
            || ProjectPicker.SelectedItem is not ProjectListItem selectedItem
            || selectedItem.Id != item.Id)
        {
            return;
        }

        _selectedProject = project;
        AddressLabel.Text = project.Address ?? string.Empty;
        ExportButton.IsEnabled = true;
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null)
        {
            MessageBox.Show("Please select a project first.", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_exportBridge is null)
        {
            ShowExportRequiresRevitMessage();
            return;
        }

        _exportBridge.ApiClient = _apiClient;
        _exportBridge.SelectedProjectId = _selectedProject.Id;
        _exportBridge.Direction = SyncDirection.Update;
        _exportBridge.Raise();
    }

    private void CreateProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLoggedIn)
        {
            MessageBox.Show("Please log in first.", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_exportBridge is null)
        {
            ShowExportRequiresRevitMessage();
            return;
        }

        _exportBridge.ApiClient = _apiClient;
        _exportBridge.SelectedProjectId = null;
        _exportBridge.Direction = SyncDirection.Create;
        _exportBridge.Raise();
    }

    private static void ShowExportRequiresRevitMessage()
    {
        MessageBox.Show(
            "Export requires the HVAKR pane to be running inside Revit.",
            "HVAKR",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private bool ShowSavedUpdateStatus()
    {
        var status = _updateService.LoadStatus();
        if (status is null) return false;

        if (status.Succeeded)
        {
            MessageBox.Show(
                $"HVAKR Revit Plugin {status.Version} was installed successfully.",
                "HVAKR",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _updateService.AcknowledgeSuccessfulUpdate(status);
            return false;
        }

        _retryingFailedUpdate = true;
        UpdateMessage.Text = $"Update {status.Version} failed: {status.Error}";
        UpdatePrimaryButton.Content = "Retry";
        UpdatePrimaryButton.IsEnabled = _canInstallUpdates;
        ReleaseNotesButton.Visibility = Visibility.Collapsed;
        UpdateLaterButton.Visibility = Visibility.Visible;
        UpdateBanner.Visibility = Visibility.Visible;
        return true;
    }

    private void ShowReadyUpdate(UpdateState state)
    {
        if (state.ReadyVersion is null || state.ReadyVersion == state.DismissedVersion) return;

        _retryingFailedUpdate = false;
        UpdateMessage.Text = $"HVAKR Revit Plugin {state.ReadyVersion} is ready to install.";
        UpdatePrimaryButton.Content = "Install after Revit closes";
        UpdatePrimaryButton.IsEnabled = _canInstallUpdates;
        ReleaseNotesButton.Visibility = string.IsNullOrWhiteSpace(state.ReleaseNotesUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateLaterButton.Visibility = Visibility.Visible;
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesButton.IsEnabled = false;
        CheckForUpdatesButton.Content = "Checking…";
        try
        {
            var result = await _updateService.CheckAsync(force: true);
            if (result.Outcome == UpdateCheckOutcome.Ready)
            {
                ShowReadyUpdate(result.State);
            }
            else if (result.Outcome == UpdateCheckOutcome.Current)
            {
                MessageBox.Show("HVAKR Revit Plugin is up to date.", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (result.Outcome == UpdateCheckOutcome.Failed)
            {
                MessageBox.Show($"The update check failed: {result.Error}", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            CheckForUpdatesButton.Content = "Check for updates";
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private void UpdatePrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _updateService.LaunchUpdater(Environment.ProcessId);
            _updateService.ClearStatus();
            UpdateMessage.Text = _retryingFailedUpdate
                ? "The update will retry after every Revit process closes."
                : "The update will install after every Revit process closes.";
            UpdatePrimaryButton.IsEnabled = false;
            ReleaseNotesButton.Visibility = Visibility.Collapsed;
            UpdateLaterButton.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to launch updater", ex);
            MessageBox.Show($"The updater could not start: {ex.Message}", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReleaseNotesButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _updateService.LoadState().ReleaseNotesUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void UpdateLaterButton_Click(object sender, RoutedEventArgs e)
    {
        _updateService.DismissReadyUpdate();
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    private interface IExportBridge
    {
        Client? ApiClient { get; set; }
        string? SelectedProjectId { get; set; }
        SyncDirection Direction { get; set; }
        void Raise();
    }

    private sealed class RevitExportBridge : IExportBridge
    {
        private readonly ExportHandler _handler = new();
        private readonly ExternalEvent _event;

        public RevitExportBridge()
        {
            _event = ExternalEvent.Create(_handler);
        }

        public Client? ApiClient
        {
            get => _handler.ApiClient;
            set => _handler.ApiClient = value;
        }

        public string? SelectedProjectId
        {
            get => _handler.SelectedProjectId;
            set => _handler.SelectedProjectId = value;
        }

        public SyncDirection Direction
        {
            get => _handler.Direction;
            set => _handler.Direction = value;
        }

        public void Raise() => _event.Raise();
    }
}
