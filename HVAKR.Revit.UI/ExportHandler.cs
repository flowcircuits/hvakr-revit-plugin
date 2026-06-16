using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using HVAKR.Api;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace HVAKR.Revit.UI;

/// <summary>
/// Extracts MEP spaces from the active Revit document and pushes them to HVAKR
/// (either creating a new project or updating the selected one).
///
/// Runs inside Revit's API context via IExternalEventHandler — never call this
/// directly from a WPF event handler; always raise an ExternalEvent.
/// </summary>
public class ExportHandler : IExternalEventHandler
{
    public Client? ApiClient { get; set; }
    public string? SelectedProjectId { get; set; }
    public SyncDirection Direction { get; set; } = SyncDirection.Update;

    public void Execute(UIApplication app)
    {
        var uiDocument = app.ActiveUIDocument;
        if (uiDocument is null) return;

        if (ApiClient is null)
        {
            TaskDialog.Show("HVAKR", "API client is not initialized. Log in first.");
            return;
        }

        var payloadJson = BuildPayloadJson(uiDocument.Document);

        try
        {
            // We're already on Revit's API thread here. Execute is synchronous, so sync-over-async
            // is unavoidable — but it's contained to this one call site. Don't propagate the pattern.
            switch (Direction)
            {
                case SyncDirection.Create:
                    ApiClient.CreateProjectFromRevitAsync(payloadJson).GetAwaiter().GetResult();
                    TaskDialog.Show("HVAKR", "Created a new HVAKR project from the Revit model.");
                    break;

                case SyncDirection.Update when !string.IsNullOrWhiteSpace(SelectedProjectId):
                    ApiClient.UpdateProjectFromRevitAsync(SelectedProjectId!, payloadJson).GetAwaiter().GetResult();
                    TaskDialog.Show("HVAKR", "Updated the selected HVAKR project from the Revit model.");
                    break;

                case SyncDirection.Update:
                    TaskDialog.Show("HVAKR", "No selected project to update.");
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled SyncDirection: {Direction}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Revit → HVAKR sync failed", ex);
            TaskDialog.Show("HVAKR", $"Sync failed: {ex.Message}");
        }
    }

    public string GetName() => "HVAKR Export Handler";

    private static string BuildPayloadJson(Document document)
    {
        var projectAddress = document.ProjectInformation.Address;
        var projectName = document.ProjectInformation.Name;
        var projectRotationDegrees = UnitUtils.ConvertFromInternalUnits(
            document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle,
            UnitTypeId.Degrees);

        var revitSpaces = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType()
            .OfType<Space>()
            .Where(space => space.Level is not null) // skip unplaced spaces
            .Select(ToPayload)
            .ToList();

        var payload = new
        {
            projectAddress,
            projectName,
            projectRotationDegrees,
            revitSpaces,
        };

        return JsonConvert.SerializeObject(
            payload,
            new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
    }

    private static object ToPayload(Space space)
    {
        var boundaries = space.GetBoundarySegments(new SpatialElementBoundaryOptions())
            ?.Select(segmentList => segmentList
                .Select(seg =>
                {
                    var curve = seg.GetCurve();
                    var p1 = curve.GetEndPoint(0);
                    var p2 = curve.GetEndPoint(1);
                    // Arcs are approximated as their chord — HVAKR only consumes straight segments.
                    return new { x1 = p1.X, y1 = p1.Y, x2 = p2.X, y2 = p2.Y };
                })
                .ToList())
            .ToList()
            ?? [];

        return new
        {
            area = space.Area,
            boundaries,
            levelElevation = space.Level!.Elevation,
            name = space.Name,
            number = space.Number,
            unboundedHeight = space.UnboundedHeight,
            uniqueId = space.UniqueId,
            volume = space.Volume,
        };
    }
}
