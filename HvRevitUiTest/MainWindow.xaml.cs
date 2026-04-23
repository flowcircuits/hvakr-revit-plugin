using HvRevitUi;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HvRevitUiTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Instantiate your user control from the shared library
            var dockablePanelMain = new DockablePaneMain();

            // Add it to the Grid in this window
            RootGrid.Children.Add(dockablePanelMain);
        }
    }
}