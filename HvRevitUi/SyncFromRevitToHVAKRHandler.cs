using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HvRevitUi
{
    public class SyncFromRevitToHVAKRHandler : IExternalEventHandler
    {
        public HvakrApi.HvakrApi? hvakrApiClient { get; set; }
        public string? selectedProjectId { get; set; }
        public string? syncType { get; set; } // "CREATE" or "UPDATE"

        public void Execute(UIApplication app)
        {
            UIDocument uiDocument = app.ActiveUIDocument;
            if (uiDocument == null)
            {
                return;
            }

            Document document = uiDocument.Document;

            // 0. retrieve project data
            var projectAddress = document.ProjectInformation.Address;
            var projectName = document.ProjectInformation.Name;
            var projectRotationDegrees = UnitUtils.ConvertFromInternalUnits( // 2025
                document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle, // 2025.3
                UnitTypeId.Degrees
            );

            // 1. retrieve all space instances
            var spaces = new FilteredElementCollector(document) // 2025.3
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .Cast<Space>()
                .ToList();


            // 2-3. Extract Level, UniqueId, boundary segments and build JSON-able structure
            var revitSpaces = new List<object>();

            foreach (var space in spaces)
            {
                double? levelElevation = space.Level?.Elevation; // feet
                if (levelElevation == null) {
                    // skip spaces that are unplaced
                    continue;
                }

                // Collect boundary segments on the default phase
                var boundaryOptions = new SpatialElementBoundaryOptions();
                IList<IList<BoundarySegment>>? listOfBoundarySegmentLists = space.GetBoundarySegments(boundaryOptions); // 2025

                var boundaries = new List<IList<object>>();
                if (listOfBoundarySegmentLists != null)
                {
                    foreach (var boundarySegmentList in listOfBoundarySegmentLists)
                    {
                        var newBoundarySegmentList = new List<object>();
                        foreach (var seg in boundarySegmentList)
                        {
                            var curve = seg.GetCurve();
                            // Only include straight segments; approximate arcs as line start->end
                            XYZ p1 = curve.GetEndPoint(0);
                            XYZ p2 = curve.GetEndPoint(1);

                            newBoundarySegmentList.Add(new {
                                x1 = p1.X,
                                y1 = p1.Y,
                                x2 = p2.X,
                                y2 = p2.Y
                            });
                        }
                        boundaries.Add(newBoundarySegmentList);
                    }
                }

                var spaceJson = new
                {
                    area = (space.Area as double?) ?? 0,
                    boundaries,
                    levelElevation,
                    name = space.Name,
                    number = space.Number,
                    unboundedHeight = space.UnboundedHeight,
                    uniqueId = space.UniqueId,
                    volume = (space.Volume as double?) ?? 0,
                };

                revitSpaces.Add(spaceJson);
            }

            var payload = new
            {
                projectAddress,
                projectName,
                projectRotationDegrees,
                revitSpaces
            };

            string jsonPayload = JsonConvert.SerializeObject(
                payload,
                new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() } // HVAKR API expects camelCase
            );

            try
            {
                if (hvakrApiClient == null)
                {
                    TaskDialog.Show("HVAKR", "HVAKR client is not initialized.");
                    return;
                }

                if (string.Equals(syncType, "CREATE", StringComparison.OrdinalIgnoreCase))
                {
                    _ = hvakrApiClient.CreateProjectWithRevitPayloadAsync(jsonPayload).ConfigureAwait(false).GetAwaiter().GetResult();
                }
                else if (string.Equals(syncType, "UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(selectedProjectId))
                    {
                        TaskDialog.Show("HVAKR", "No selected project to update.");
                        return;
                    }
                    _ = hvakrApiClient.UpdateProjectWithRevitPayloadAsync(selectedProjectId!, jsonPayload).ConfigureAwait(false).GetAwaiter().GetResult();
                }
                else
                {
                    TaskDialog.Show("HVAKR", "Invalid sync type. Use CREATE or UPDATE.");
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("HVAKR", $"Sync failed: {ex.Message}");
            }
        }

        public string GetName()
        {
            return "Sync From Revit To HVAKR External Event Handler";
        }
    }
}

