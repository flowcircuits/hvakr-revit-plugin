using System;
using System.Collections.Generic;

namespace HvakrApi.Models
{
    public class SpaceDetails
    {
        public string? Id { get; set; }

        public Dictionary<string, Edge> Edges { get; set; }
        public double Level { get; set; }
        public string? Name { get; set; }
        public string? Number { get; set; }
        public double? SlabHeight { get; set; } // in inches
    }

    public class Edge
    {
        public double Index { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public string? WallTypeId { get; set; }
        public string? Name { get; set; }
        public bool? ApplyLoadToCeiling { get; set; }
        public Dictionary<string, Window>? Windows { get; set; }
    }

    public class Window
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string? WindowTypeId { get; set; }
        public bool? ExternalShading { get; set; }
        public bool? InternalShading { get; set; }
        public ExternalShadingData? ExternalShadingData { get; set; }
        public InternalShadingData? InternalShadingData { get; set; }
    }

    public class ExternalShadingData
    {
        public double? TopOverhangDepth { get; set; }
        public double? TopOverhangOffset { get; set; }
        public double? SideOverhangDepth { get; set; }
        public double? SideOverhangOffset { get; set; }
    }

    public class InternalShadingData
    {
        public double? RadiantFraction { get; set; }
        public double? BeamIAC0 { get; set; }
        public double? BeamIAC60 { get; set; }
        public double? DiffuseIAC { get; set; }
    }
}
