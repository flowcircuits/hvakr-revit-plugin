using System.Reflection;
using Autodesk.Revit.UI;
using HVAKR.Revit.UI;

namespace HVAKR.Revit;

public class App : IExternalApplication
{
    // Stable identifier — Revit persists dock position under this GUID.
    // Do not change; changing it orphans every user's existing layout.
    public static readonly Guid DockablePaneGuid = new("3c649293-cdeb-4415-bbf1-d037ed56ba4e");

    private const string TabName = "HVAKR";
    private const string PanelName = "HVAKR";

    public Result OnStartup(UIControlledApplication application)
    {
        TryCreateRibbonTab(application, TabName);

        var panel = application.CreateRibbonPanel(TabName, PanelName);
        panel.AddItem(new PushButtonData(
            name: "HVAKR_Toggle",
            text: "Show",
            assemblyLocation: Assembly.GetExecutingAssembly().Location,
            className: typeof(ToggleDockablePaneCommand).FullName)
        {
            ToolTip = "Show or hide the HVAKR dockable pane",
        });

        application.RegisterDockablePane(
            new DockablePaneId(DockablePaneGuid),
            "HVAKR",
            new PaneProvider(new MainPane()));

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    private static void TryCreateRibbonTab(UIControlledApplication application, string name)
    {
        try
        {
            application.CreateRibbonTab(name);
        }
        catch
        {
            // Revit throws if the tab already exists. That's fine.
        }
    }

    private sealed class PaneProvider(MainPane pane) : IDockablePaneProvider
    {
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = pane;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
        }
    }
}
