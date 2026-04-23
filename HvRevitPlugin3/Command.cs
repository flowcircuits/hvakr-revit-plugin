using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;


namespace HvRevitPlugin3
{


    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Get the dockable pane using the GUID defined earlier
            DockablePaneId paneId = new DockablePaneId(App.DockablePaneGuid);
            DockablePane pane = commandData.Application.GetDockablePane(paneId);

            // Toggle the pane: hide it if shown, or show it if hidden.
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
}
