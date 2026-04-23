using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;

using HvRevitUi;


namespace HvRevitPlugin3
{
    public class App : IExternalApplication
    {
        public static readonly Guid DockablePaneGuid = new Guid("3c649293-cdeb-4415-bbf1-d037ed56ba4e");

        public Result OnStartup(UIControlledApplication application)
        {
            // 1. Create or reuse a tab named "HVAKR"
            const string tabName = "HVAKR";
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // If the tab already exists, Revit throws an exception we can safely ignore.
            }

            // 2. Create a panel under that tab. 
            RibbonPanel ribbonPanel = application.CreateRibbonPanel(tabName, "HVAKR");

            // 3. Define a push button for your ExternalCommand
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData(
                "HVAKR_Button",
                "Show",
                assemblyPath,
                "HvRevitPlugin3.Command"
            );

            // Optional: Assign a 32×32 icon as a "large" image
            // (Make sure the PNG is properly embedded or accessible via pack://)
            // buttonData.LargeImage = new BitmapImage(
            //     new Uri("pack://application:,,,/HvRevitPlugin3;component/HVAKR.png")
            // );

            // Tooltip to clarify what the button does
            buttonData.ToolTip = "Show or hide the HVAKR dockable pane";

            // 4. Add the push button to the panel
            ribbonPanel.AddItem(buttonData);

            // 5. (Optional) Register your dockable pane, if you haven’t already
            //    This is where you supply the WPF UserControl, etc.
            DockablePaneProviderData providerData = new DockablePaneProviderData
            {
                FrameworkElement = new DockablePaneMain(),
                InitialState = new DockablePaneState
                {
                    DockPosition = DockPosition.Right
                }
            };
            application.RegisterDockablePane(
                new DockablePaneId(DockablePaneGuid),
                "HVAKR",
                new DockablePaneProvider(providerData)
            );

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Perform any necessary cleanup here.
            return Result.Succeeded;
        }
    }
}
