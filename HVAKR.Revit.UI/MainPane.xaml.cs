using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private Dictionary<string, ProjectSummary> _projectsById = new();
    private ProjectSummary? _selectedProject;
    private bool _isLoggedIn;
    private bool _retryingFailedUpdate;

    public MainPane() : this(new RevitExportBridge())
    {
    }

    private MainPane(IExportBridge? exportBridge)
    {
        InitializeComponent();
        VersionText.Text = $"v{_updateService.CurrentVersion}";
        _exportBridge = exportBridge;
        _canInstallUpdates = exportBridge is not null;
        UpdateLoggedInState();
        Loaded += MainPane_Loaded;
    }

    public static MainPane CreateForHarness() => new(exportBridge: null);

    public void ApplyTheme(bool dark) =>
        Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"/HVAKR.Revit.UI;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };

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
        LoginView.Visibility = _isLoggedIn ? Visibility.Collapsed : Visibility.Visible;
        MainView.Visibility = _isLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        HeaderLogo.Visibility = _isLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        AccountButton.Visibility = _isLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        UpdateBanner.Margin = _isLoggedIn ? new Thickness(12, 0, 12, 12) : new Thickness(12);

        if (!_isLoggedIn)
        {
            ProjectPicker.ItemsSource = null;
            AddressLabel.Text = string.Empty;
            AccountMenu.IsOpen = false;
            ApiKeyBox.Password = string.Empty;
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Password.Trim();
        if (apiKey.Length == 0)
        {
            ShowLoginError("Enter your HVAKR API key.");
            return;
        }

        LoginError.Visibility = Visibility.Collapsed;
        LoginButton.IsEnabled = false;
        LoginButton.Content = "Logging in…";
        try
        {
            await LoginAsync(apiKey);
        }
        catch (Exception ex)
        {
            Logger.LogError("Login failed", ex);
            Logout();
            ShowLoginError(ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
                ? "That API key isn't valid."
                : "Couldn't log in. Check your connection and try again.");
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Content = "Log in";
        }
    }

    private async Task LoginAsync(string apiKey)
    {
        _httpClient = new HttpClient();
        _apiClient = new Client(_httpClient, apiKey);

        var meTask = _apiClient.GetMeAsync();
        var projectsTask = _apiClient.GetProjectsAsync();
        await Task.WhenAll(meTask, projectsTask);
        var me = meTask.Result;
        var projects = projectsTask.Result;
        if (projects.Count == 0)
        {
            Logout();
            ShowLoginError("No projects found for this account.");
            return;
        }

        var name = $"{me.FirstName} {me.LastName}".Trim();
        AccountName.Text = name.Length > 0 ? name : me.Email ?? string.Empty;
        AccountEmail.Text = me.Email ?? string.Empty;
        AvatarInitials.Text = GetInitials(me);
        AccountButton.ToolTip = AccountName.Text;

        _projectsById = projects.ToDictionary(project => project.Id);
        ProjectPicker.ItemsSource = projects
            .Select(p => new ProjectListItem(p.Id, p.Name ?? p.Number ?? p.Id))
            .ToList();
        ProjectPicker.SelectedIndex = 0;

        _isLoggedIn = true;
        UpdateLoggedInState();
    }

    private void ShowLoginError(string message)
    {
        LoginError.Text = message;
        LoginError.Visibility = Visibility.Visible;
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ApiKeyPlaceholder.Visibility = ApiKeyBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        LoginError.Visibility = Visibility.Collapsed;
    }

    private void ApiKeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !LoginButton.IsEnabled) return;
        e.Handled = true;
        LoginButton_Click(LoginButton, e);
    }

    private static string GetInitials(MeUser me)
    {
        var initials = string.Concat(new[] { me.FirstName, me.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => char.ToUpperInvariant(part!.Trim()[0])));
        if (initials.Length > 0) return initials;
        return string.IsNullOrEmpty(me.Email) ? "?" : char.ToUpperInvariant(me.Email[0]).ToString();
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e) => AccountMenu.IsOpen = !AccountMenu.IsOpen;

    private void LogoutButton_Click(object sender, RoutedEventArgs e) => Logout();

    private void Logout()
    {
        _isLoggedIn = false;
        _apiClient = null;
        _selectedProject = null;
        _projectsById = new Dictionary<string, ProjectSummary>();
        _httpClient?.Dispose();
        _httpClient = null;
        UpdateLoggedInState();
    }

    private void ProjectPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectPicker.SelectedItem is not ProjectListItem item) return;
        _selectedProject = null;
        AddressLabel.Text = string.Empty;
        ExportButton.IsEnabled = false;

        if (!_projectsById.TryGetValue(item.Id, out var project)) return;

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
        CheckForUpdatesButton.ToolTip = "Checking for updates…";
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
            CheckForUpdatesButton.ToolTip = "Check for updates";
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
