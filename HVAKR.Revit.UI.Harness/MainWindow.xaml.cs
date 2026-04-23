using System.Windows;

namespace HVAKR.Revit.UI.Harness;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RootGrid.Children.Add(MainPane.CreateForHarness());
    }
}
