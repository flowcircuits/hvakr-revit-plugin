using System.Windows;
using System.Windows.Controls;
using HVAKR.Api;
using HVAKR.Api.Models;
using ExternalEvent = Autodesk.Revit.UI.ExternalEvent;

namespace HVAKR.Revit.UI;

public partial class MainPane : UserControl
{
    private readonly IExportBridge? _exportBridge;

    private HttpClient? _httpClient;
    private Client? _apiClient;
    private Dictionary<string, ProjectDetails> _projectsById = new();
    private ProjectDetails? _selectedProject;
    private bool _isLoggedIn;

    public MainPane() : this(new RevitExportBridge())
    {
    }

    private MainPane(IExportBridge? exportBridge)
    {
        InitializeComponent();
        _exportBridge = exportBridge;
        UpdateLoggedInState();
    }

    public static MainPane CreateForHarness() => new(exportBridge: null);

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

            var projectIds = await _apiClient.GetProjectIdsAsync();
            if (projectIds.Count == 0)
            {
                MessageBox.Show("No projects found for this account.", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Warning);
                Logout();
                return;
            }

            var accessible = new Dictionary<string, ProjectDetails>();
            foreach (var id in projectIds)
            {
                try
                {
                    var details = await _apiClient.GetProjectDetailsAsync(id);
                    if (details?.Id is not null)
                    {
                        accessible[details.Id] = details;
                    }
                }
                catch
                {
                    // Skip projects we can't access (e.g. insufficient permissions on a specific one).
                }
            }

            if (accessible.Count == 0)
            {
                MessageBox.Show("No accessible projects found.", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Warning);
                Logout();
                return;
            }

            _projectsById = accessible;

            var items = accessible.Values
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new ProjectListItem(p.Id!, p.Name!))
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

        if (!_projectsById.TryGetValue(item.Id, out var project)) return;

        // Lazily load ?expand=true the first time a project is selected.
        if (project.Spaces is null)
        {
            try
            {
                var expanded = await _apiClient.GetProjectDetailsAsync(item.Id, expand: true);
                if (expanded is not null)
                {
                    project = expanded;
                    _projectsById[item.Id] = expanded;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to expand project {item.Id}", ex);
            }
        }

        _selectedProject = project;
        AddressLabel.Text = project.Address ?? string.Empty;
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
