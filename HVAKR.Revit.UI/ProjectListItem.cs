namespace HVAKR.Revit.UI;

/// <summary>
/// Minimal shape bound to the project ComboBox. We used to pass an anonymous type
/// and read it back with <c>dynamic</c>; this is the typed replacement.
/// </summary>
public sealed record ProjectListItem(string Id, string Name);
