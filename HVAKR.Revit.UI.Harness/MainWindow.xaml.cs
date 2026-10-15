using System.Windows;

namespace HVAKR.Revit.UI.Harness;

public partial class MainWindow : Window
{
    private readonly MainPane _pane = MainPane.CreateForHarness();

    public MainWindow()
    {
        InitializeComponent();
        RootGrid.Children.Add(_pane);
    }

    private void DarkThemeToggle_Changed(object sender, RoutedEventArgs e) =>
        _pane.ApplyTheme(DarkThemeToggle.IsChecked == true);
}
