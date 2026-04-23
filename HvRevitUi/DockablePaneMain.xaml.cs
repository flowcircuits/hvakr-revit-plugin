using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExternalEvent = Autodesk.Revit.UI.ExternalEvent;
using System.Linq;
using System.Threading.Tasks;
using HvakrApi.Models;

namespace HvRevitUi
{

    /// <summary>
    /// Interaction logic for DockablePaneMain.xaml
    /// </summary>
    ///
    public partial class DockablePaneMain : UserControl
    {
        private HvakrApi.Models.ProjectDetails[]? loadedProjectDetails;
        private HvakrApi.Models.ProjectDetails? selectedProjectDetails;
        private HvakrApi.HvakrApi? hvakrApi;
        private HttpClient? sharedHttpClient;
        private bool isUserLoggedIn = false;

        private string apiKeyHiddenText = "**************";

        private readonly SyncFromHVAKRToRevitHandler syncToRevitHandler;
        private readonly ExternalEvent addSpacesEvent;
        private readonly SyncFromRevitToHVAKRHandler syncToHvakrHandler;
        private readonly ExternalEvent syncToHvakrEvent;


        public DockablePaneMain()
        {
            InitializeComponent();

            syncToRevitHandler = new SyncFromHVAKRToRevitHandler();
            addSpacesEvent = ExternalEvent.Create(syncToRevitHandler);

            syncToHvakrHandler = new SyncFromRevitToHVAKRHandler();
            syncToHvakrEvent = ExternalEvent.Create(syncToHvakrHandler);
        }

        private void UpdatePaneState()
        {
            if (isUserLoggedIn)
            {
                ButtonLogin.Content = "Logout";
                ButtonLogin.IsEnabled = true;
                ButtonLogin.IsTabStop = false;

                ComboBoxProject.IsEnabled = true;

                TextBoxApiKey.IsEnabled = false;
                TextBoxApiKey.Text = apiKeyHiddenText;

                //TextBoxOffsetX.Text = "0";
                //TextBoxOffsetY.Text = "0";
                //TextBoxOffsetZ.Text = "0";

                //ButtonAddHVAKRSpacesToRevit.IsEnabled = true;
                ButtonExportToSelectedProject.IsEnabled = true;
                ButtonCreateHVAKRProject.IsEnabled = true;

                DetailsPanel.IsEnabled = true;
            }
            else
            {
                ButtonLogin.Content = "Login";
                ButtonLogin.IsEnabled = true;
                ButtonLogin.IsTabStop = true;

                ComboBoxProject.ItemsSource = null;
                ComboBoxProject.IsEnabled = false;
                TextBlockAddress.Text = "";

                TextBoxApiKey.IsEnabled = true;
                TextBoxApiKey.Text = "Edit...";

                //TextBoxOffsetX.Text = "";
                //TextBoxOffsetY.Text = "";
                //TextBoxOffsetZ.Text = "";

                //ButtonAddHVAKRSpacesToRevit.IsEnabled = false;
                ButtonExportToSelectedProject.IsEnabled = false;
                ButtonCreateHVAKRProject.IsEnabled = false;

                DetailsPanel.IsEnabled = false;
            }
        }

        private void TextBoxApiKey_GotFocus(object sender, RoutedEventArgs e)
        {
            // Clear the TextBox when clicked if it contains the placeholder text
            if (TextBoxApiKey.Text == "Edit...")
            {
                TextBoxApiKey.Text = "";
            }
        }

        private void TextBoxOffset_GotFocus(object sender, RoutedEventArgs e)
        {
            // Clear the TextBox when clicked if it contains 0
            if (sender is TextBox tb)
            {
                if (tb.Text == "0")
                {
                    tb.Text = "";
                }
            }
        }

        private void TextBoxOffset_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                TextBoxOffset_HandleDone(tb);
            }
        }

        private void TextBoxOffset_KeyDown(object sender, RoutedEventArgs e)
        {
            if (e is KeyEventArgs keyEvent && keyEvent.Key == Key.Enter && sender is TextBox tb)
            {
                TextBoxOffset_HandleDone(tb);

                // Move focus to the next focusable control
                TraversalRequest request = new TraversalRequest(FocusNavigationDirection.Next);
                UIElement? focusedElement = Keyboard.FocusedElement as UIElement;
                if (focusedElement != null)
                {
                    focusedElement.MoveFocus(request);
                }

                keyEvent.Handled = true;
            }
        }

        private void TextBoxOffset_HandleDone(TextBox textBox)
        {
            if (textBox.Text == "")
            {
                textBox.Text = "0";
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (isUserLoggedIn == true)
            {
                // user is trying to logout. just reset all the variables
                isUserLoggedIn = false;
                UpdatePaneState();
            }
            else
            {
                // user is logged in

                // Retrieve the API key from the TextBox
                string apiKey = TextBoxApiKey.Text;

                TextBoxApiKey.Text = apiKeyHiddenText;
                ButtonLogin.IsEnabled = false;
                ButtonLogin.Content = "Loading...";

                if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "Edit...")
                {
                    UpdatePaneState();
                    MessageBox.Show("Please enter a valid API key.", "Login", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    // Create an HttpClient and initialize your API wrapper.
                    sharedHttpClient = new HttpClient();
                    hvakrApi = new HvakrApi.HvakrApi(sharedHttpClient, apiKey);
                    var projectIds = new List<string>();
                    try
                    {
                        projectIds = await hvakrApi.GetProjectIdsAsync();
                    }
                    catch (Exception)
                    {
                        UpdatePaneState();
                        MessageBox.Show($"Login failed.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    if (projectIds == null || !projectIds.Any())
                    {
                        UpdatePaneState();
                        MessageBox.Show("No projects found.", "HVAKR Login", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // now get the name of each project so there is something pretty to put in the drop down
                    var projectDetailsList = new List<ProjectDetails>();
                    foreach (var id in projectIds)
                    {
                        try
                        {
                            var details = await hvakrApi.GetProjectDetailsAsync(id);
                            if (details != null)
                                projectDetailsList.Add(details);
                        }
                        catch (Exception)
                        {
                            // Skip projects we can't access (e.g. insufficient permissions)
                        }
                    }

                    if (!projectDetailsList.Any())
                    {
                        UpdatePaneState();
                        MessageBox.Show("No accessible projects found.", "HVAKR Login", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    loadedProjectDetails = projectDetailsList.ToArray();
                    var projectNames = loadedProjectDetails
                        .Where(pd => !string.IsNullOrWhiteSpace(pd.Name))
                        .Select(pd => new { pd.Id, pd.Name })
                        .ToList();

                    // Populate the ComboBox with project names
                    ComboBoxProject.ItemsSource = null;
                    ComboBoxProject.ItemsSource = projectNames;
                    ComboBoxProject.DisplayMemberPath = "Name";
                    ComboBoxProject.SelectedIndex = 0;

                    TextBlockAddress.Text = loadedProjectDetails[0].Address;

                    // now update the
                    isUserLoggedIn = true;
                    UpdatePaneState();
                }
                catch (Exception ex)
                {
                    isUserLoggedIn = false;
                    UpdatePaneState();
                    MessageBox.Show($"An error occurred during login: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ComboBoxProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = (ComboBox)sender;
            var selected = combo.SelectedItem;
            if (selected != null)
            {
                // Get the selected project ID
                var selectedProject = (dynamic)selected;
                string selectedProjectId = selectedProject.Id;
                // Find the corresponding project details
                selectedProjectDetails = loadedProjectDetails?.First(pd => pd.Id == selectedProjectId);
                if (selectedProjectDetails != null)
                {

                    if (selectedProjectDetails.ExpandLoaded == null || selectedProjectDetails.ExpandLoaded == false)
                    {
                        // if expand is not loaded, make an API call to get the full project details
                        selectedProjectDetails = await hvakrApi!.GetProjectDetailsAsync(selectedProjectId, true);
                        selectedProjectDetails.ExpandLoaded = true;

                        // update the loadedProjectDetails array
                        loadedProjectDetails = loadedProjectDetails!.Select(pd => pd.Id == selectedProjectId ? selectedProjectDetails : pd).ToArray();
                    }

                    // Update the UI with the selected project's details
                    TextBlockAddress.Text = selectedProjectDetails.Address;
                }
            }
        }

        /**
        private void AddHVAKRSpacesToRevitButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedProjectDetails == null)
            {
                MessageBox.Show("Please select a project first.", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Capture inputs
            double.TryParse(TextBoxOffsetX.Text, out double offsetX);
            double.TryParse(TextBoxOffsetY.Text, out double offsetY);
            double.TryParse(TextBoxOffsetZ.Text, out double offsetZ);

            // Pass data into handler and raise event to run in Revit API context
            syncToRevitHandler.SelectedProjectDetails = selectedProjectDetails;
            syncToRevitHandler.OffsetX = offsetX;
            syncToRevitHandler.OffsetY = offsetY;
            syncToRevitHandler.OffsetZ = offsetZ;

            addSpacesEvent.Raise();
        }
        */

        private void ExportToSelectedProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedProjectDetails == null)
            {
                MessageBox.Show("Please select a project first.", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            syncToHvakrHandler.hvakrApiClient = hvakrApi;
            syncToHvakrHandler.selectedProjectId = selectedProjectDetails?.Id;
            syncToHvakrHandler.syncType = "UPDATE";
            syncToHvakrEvent.Raise();
        }

        private void CreateHVAKRProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isUserLoggedIn)
            {
                MessageBox.Show("Please login first.", "HVAKR", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            syncToHvakrHandler.hvakrApiClient = hvakrApi;
            syncToHvakrHandler.selectedProjectId = null;
            syncToHvakrHandler.syncType = "CREATE";
            syncToHvakrEvent.Raise();
        }
    }
}
