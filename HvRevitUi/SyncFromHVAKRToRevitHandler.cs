using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HvRevitUi
{
    public class SyncFromHVAKRToRevitHandler : IExternalEventHandler
    {
        public HvakrApi.Models.ProjectDetails? SelectedProjectDetails { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double OffsetZ { get; set; }

        public void Execute(UIApplication app)
        {
            UIDocument uiDocument = app.ActiveUIDocument;
            if (uiDocument == null)
            {
                return;
            }

            Document document = uiDocument.Document;

            using (Transaction transaction = new Transaction(document, "Add HVAKR Spaces"))
            {
                transaction.Start();
                if (SelectedProjectDetails == null || SelectedProjectDetails.Spaces == null || SelectedProjectDetails.Spaces.Count == 0)
                {
                    transaction.Commit();
                    return;
                }

                // Get all levels in the model
                List<Level> revitLevels = new List<Level>();
                FilteredElementCollector levelCollector = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Levels)
                    .WhereElementIsNotElementType();
                foreach (Level level in levelCollector)
                {
                    revitLevels.Add(level);
                }

                // Sort levels by elevation
                revitLevels = revitLevels.OrderBy(l => l.Elevation).ToList();

                // 1) Group HVAKR spaces by Level (unique elevations)
                var spacesByLevel = SelectedProjectDetails.Spaces
                    .Values
                    .GroupBy(s => s.Level)
                    .OrderBy(g => g.Key)
                    .ToList();

                // 2) Ensure we have at least as many Revit levels as HVAKR levels (create extras if needed)
                int hvakrLevelCount = spacesByLevel.Count;

                double lastElevation = revitLevels.Count > 0 ? revitLevels.Last().Elevation : 0.0;
                while (revitLevels.Count < hvakrLevelCount)
                {
                    // Recalculate spacing for this step based on HVAKR group that aligns to the current top Revit level
                    double levelDelta = 10.0; // feet fallback
                    int hvakrIndexForTopRevit = revitLevels.Count - 1;
                    if (hvakrIndexForTopRevit >= 0)
                    {
                        var topGroup = spacesByLevel[hvakrIndexForTopRevit];
                        double maxSlabInches = topGroup.Max(s => s.SlabHeight ?? 0.0);
                        if (maxSlabInches > 0)
                        {
                            levelDelta = maxSlabInches / 12.0; // inches -> feet
                        }
                    }
                    else
                    {
                        levelDelta = 0.0;
                    }

                    lastElevation += levelDelta;
                    Level newLevel = Level.Create(document, lastElevation);
                    revitLevels.Add(newLevel);
                }

                // Keep levels sorted from lowest to highest
                revitLevels = revitLevels.OrderBy(l => l.Elevation).ToList();

                // Map HVAKR levels to Revit levels by index (lowest to lowest, etc.)
                var hvakrLevelElevationToRevitLevel = new Dictionary<double, Level>();
                for (int i = 0; i < hvakrLevelCount; i++)
                {
                    hvakrLevelElevationToRevitLevel[spacesByLevel[i].Key] = revitLevels[i];
                }

                // Prepare Space Separation Line type (category OST_SpaceSeparationLines)
                CurveElementType? spaceSeparationType = null;

                // For each HVAKR level group, create separation lines and spaces
                foreach (var group in spacesByLevel)
                {
                    double hvakrLevelElevation = group.Key;
                    Level level = hvakrLevelElevationToRevitLevel[hvakrLevelElevation];

                    // Create/reuse a sketch plane at this level elevation
                    double z = level.Elevation;
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z));
                    SketchPlane sketchPlane = SketchPlane.Create(document, plane);

                    foreach (var spaceDetails in group)
                    {
                        // 3) Create space separation lines from edges
                        var vertices = new List<XYZ>();

                        if (spaceDetails.Edges != null && spaceDetails.Edges.Count > 0)
                        {
                            foreach (var edge in spaceDetails.Edges.Values.OrderBy(e => e.Index))
                            {
                                double x1 = edge.X1 + OffsetX;
                                double y1 = edge.Y1 + OffsetY;
                                double x2 = edge.X2 + OffsetX;
                                double y2 = edge.Y2 + OffsetY;

                                XYZ p1 = new XYZ(x1, y1, z);
                                XYZ p2 = new XYZ(x2, y2, z);

                                vertices.Add(p1);
                                vertices.Add(p2);

                                Line line = Line.CreateBound(p1, p2);

                                if (spaceSeparationType != null)
                                {
                                    //CurveElement.Create(document, spaceSeparationType.Id, sketchPlane.Id, line);
                                }
                                else
                                {
                                    // Fallback: create a model curve so that a boundary exists even if specific type is missing
                                    document.Create.NewModelCurve(line, sketchPlane);
                                }
                            }
                        }

                        // Compute a simple centroid from unique vertices (average)
                        XYZ placementPoint;
                        if (vertices.Count > 0)
                        {
                            // Deduplicate by XY to avoid overweighting endpoints
                            var uniqueByXY = vertices
                                .GroupBy(v => (Math.Round(v.X, 6), Math.Round(v.Y, 6)))
                                .Select(g => g.First())
                                .ToList();

                            double cx = uniqueByXY.Average(v => v.X);
                            double cy = uniqueByXY.Average(v => v.Y);
                            placementPoint = new XYZ(cx, cy, z);
                        }
                        else
                        {
                            // Default placement at level origin if no edges provided
                            placementPoint = new XYZ(0, 0, z);
                        }

                        // Create the Space at the computed location
                        UV uv = new UV(placementPoint.X, placementPoint.Y);
                        Space newSpace = document.Create.NewSpace(level, uv);

                        if (!string.IsNullOrWhiteSpace(spaceDetails.Name))
                        {
                            newSpace.Name = spaceDetails.Name;
                        }
                        if (!string.IsNullOrWhiteSpace(spaceDetails.Number))
                        {
                            newSpace.Number = spaceDetails.Number;
                        }
                    }
                }

                transaction.Commit();
            }
        }

        public string GetName()
        {
            return "Add HVAKR Spaces External Event Handler";
        }
    }
}

