using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HvRevitPlugin3
{
    public class DockablePaneProvider : IDockablePaneProvider
    {
        private readonly DockablePaneProviderData _providerData;

        public DockablePaneProvider(DockablePaneProviderData providerData)
        {
            _providerData = providerData;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            // Pass along the framework element and initial state from the provider data.
            data.FrameworkElement = _providerData.FrameworkElement;
            data.InitialState = _providerData.InitialState;
        }
    }
}

