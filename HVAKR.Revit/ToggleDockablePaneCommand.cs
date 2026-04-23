using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HVAKR.Revit;

[Transaction(TransactionMode.Manual)]
public class ToggleDockablePaneCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var pane = commandData.Application.GetDockablePane(new DockablePaneId(App.DockablePaneGuid));
        if (pane.IsShown())
        {
            pane.Hide();
        }
        else
        {
            pane.Show();
        }
        return Result.Succeeded;
    }
}
