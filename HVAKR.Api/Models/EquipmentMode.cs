namespace HVAKR.Api.Models;

/// <summary>
/// A project-level operating mode introduced by the modular equipment config API.
/// The plugin does not edit modes, but keeps the response shape typed when a
/// project is fetched from the API.
/// </summary>
public sealed class EquipmentMode
{
    public string? Id { get; set; }
    public string? LoadCondition { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}
