namespace HVAKR.Api.Models;

public class ProjectListResponse
{
    public List<ProjectSummary> Projects { get; set; } = [];
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
}

public class ProjectSummary
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Address { get; set; }
    public string? Status { get; set; }
    public string? ProjectType { get; set; }
    public double? Timestamp { get; set; }
    public double? LastOpenTime { get; set; }
}
