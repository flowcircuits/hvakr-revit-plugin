using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using HVAKR.Api;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace HVAKR.Revit.UI;

/// <summary>
/// Extracts MEP spaces (or, when the model has none, rooms or linked spaces and rooms) from the
/// active Revit document and pushes them to HVAKR
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

        try
        {
            var (payloadJson, spatialSource) = BuildPayloadJson(uiDocument.Document);
            var exported = spatialSource.Elements.Count == 0
                ? "No placed MEP spaces or rooms were found in this model or its loaded links, so only the project information was exported."
                : $"Exported {spatialSource.Elements.Count} {spatialSource.Description}.";

            // We're already on Revit's API thread here. Execute is synchronous, so sync-over-async
            // is unavoidable — but it's contained to this one call site. Don't propagate the pattern.
            switch (Direction)
            {
                case SyncDirection.Create:
                    ApiClient.CreateProjectFromRevitAsync(payloadJson).GetAwaiter().GetResult();
                    TaskDialog.Show("HVAKR", $"Created a new HVAKR project from the Revit model. {exported}");
                    break;

                case SyncDirection.Update when !string.IsNullOrWhiteSpace(SelectedProjectId):
                    ApiClient.UpdateProjectFromRevitAsync(SelectedProjectId!, payloadJson).GetAwaiter().GetResult();
                    TaskDialog.Show("HVAKR", $"Updated the selected HVAKR project from the Revit model. {exported}");
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

    private static (string Json, SpatialSource Source) BuildPayloadJson(Document document)
    {
        var projectAddress = document.ProjectInformation.Address;
        var projectName = document.ProjectInformation.Name;
        var projectRotationDegrees = UnitUtils.ConvertFromInternalUnits(
            document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle,
            UnitTypeId.Degrees);

        var source = CollectSpatialElements(document);
        var revitSpaces = source.Elements.Select(ToPayload).ToList();

        var payload = new
        {
            projectAddress,
            projectName,
            projectRotationDegrees,
            revitSpaces,
        };

        var json = JsonConvert.SerializeObject(
            payload,
            new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
        return (json, source);
    }

    /// <summary>
    /// Exports the host model's spaces or rooms when it has any that are enclosed. Otherwise it exports
    /// those of each loaded Revit link, transformed into host coordinates. Cloud models are usually one
    /// discipline model of a federated set, so the host often has no usable MEP spaces: the rooms are in
    /// the host architectural model, or in a linked one. Host and links are never combined, because host
    /// MEP spaces are usually drawn over the linked rooms, and both would then be exported.
    /// </summary>
    private static SpatialSource CollectSpatialElements(Document document)
    {
        var (hostKind, hostElements) = DocumentSpatialElements(document);
        if (hostElements.Count > 0)
            return new SpatialSource(hostKind, hostElements.Select(e => new SourcedElement(e, Transform.Identity, null)).ToList());

        var linked = new FilteredElementCollector(document)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .Where(link => link.GetLinkDocument() is not null) // skip unloaded links
            .SelectMany(link =>
            {
                var transform = link.GetTotalTransform();
                // Prefix with the link instance so two placements of the same link stay distinct in HVAKR.
                return DocumentSpatialElements(link.GetLinkDocument()).Elements
                    .Select(e => new SourcedElement(e, transform, link.UniqueId + "/"));
            })
            .ToList();
        return new SpatialSource("spaces and rooms from linked models (the model has no enclosed MEP spaces or rooms)", linked);
    }

    /// <summary>
    /// MEP spaces of one document, or its rooms when it has no enclosed MEP space. A stray not-enclosed
    /// space (area 0) does not hide the rooms.
    /// </summary>
    private static (string Kind, List<SpatialElement> Elements) DocumentSpatialElements(Document document)
    {
        var spaces = PlacedSpatialElements(document, BuiltInCategory.OST_MEPSpaces);
        return spaces.Count > 0
            ? ("MEP spaces", spaces)
            : ("rooms (the model has no enclosed MEP spaces)", PlacedSpatialElements(document, BuiltInCategory.OST_Rooms));
    }

    /// <summary>
    /// Placed elements of one category in the latest phase that has an enclosed one. Revit keeps a
    /// separate room or space per phase, so more than one phase would export the same room twice.
    /// </summary>
    private static List<SpatialElement> PlacedSpatialElements(Document document, BuiltInCategory category)
    {
        var phaseOrder = document.Phases
            .Cast<Phase>()
            .Select((phase, index) => (phase.Id.Value, index))
            .ToDictionary(phase => phase.Value, phase => phase.index);
        var placed = new FilteredElementCollector(document)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .OfType<SpatialElement>()
            .Where(element => element.Location is not null && element.Level is not null); // skip unplaced elements

        // Rooms and MEP spaces both store their phase in ROOM_PHASE.
        return SpatialSelection.InLatestEnclosedPhase(
            placed,
            element => element.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId() is { } phaseId
                && phaseOrder.TryGetValue(phaseId.Value, out var index)
                    ? index
                    : -1,
            element => element.Area > 0);
    }

    private static object ToPayload(SourcedElement sourced)
    {
        var (element, transform, idPrefix) = sourced;
        var boundaries = element.GetBoundarySegments(new SpatialElementBoundaryOptions())
            ?.Select(segmentList => segmentList
                .Select(seg =>
                {
                    var curve = seg.GetCurve();
                    var p1 = transform.OfPoint(curve.GetEndPoint(0));
                    var p2 = transform.OfPoint(curve.GetEndPoint(1));
                    // Arcs are approximated as their chord — HVAKR only consumes straight segments.
                    return new { x1 = p1.X, y1 = p1.Y, x2 = p2.X, y2 = p2.Y };
                })
                .ToList())
            .ToList()
            ?? [];

        var (unboundedHeight, volume) = element switch
        {
            Space space => (space.UnboundedHeight, space.Volume),
            Room room => (room.UnboundedHeight, room.Volume),
            _ => throw new InvalidOperationException($"Unexpected spatial element type: {element.GetType().Name}"),
        };

        return new
        {
            area = element.Area,
            boundaries,
            // ProjectElevation is relative to the internal origin, which the link transform maps into
            // host coordinates. HVAKR only groups and orders levels by this value.
            levelElevation = transform.Origin.Z + element.Level!.ProjectElevation,
            name = element.Name,
            number = element.Number,
            unboundedHeight,
            uniqueId = idPrefix + element.UniqueId,
            volume,
        };
    }

    private sealed record SourcedElement(SpatialElement Element, Transform Transform, string? IdPrefix);

    private sealed record SpatialSource(string Description, List<SourcedElement> Elements);
}
