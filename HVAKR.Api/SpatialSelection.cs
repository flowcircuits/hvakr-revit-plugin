namespace HVAKR.Api;

/// <summary>
/// Revit-free rules for which spatial elements (MEP spaces or rooms) of one document to export.
/// Kept here, not in the Revit handler, so the rules have unit tests.
/// </summary>
public static class SpatialSelection
{
    /// <summary>
    /// Returns the elements of the latest phase that has at least one enclosed element (area > 0).
    /// Revit keeps a separate room or space per phase, each with its own UniqueId, so exporting more
    /// than one phase would create the same room twice in HVAKR. Returns nothing when no element is
    /// enclosed: a model with only not-enclosed elements has nothing usable to export.
    /// </summary>
    /// <param name="phaseIndex">Position of the element's phase in the document's phase order; -1 when unknown.</param>
    public static List<T> InLatestEnclosedPhase<T>(IEnumerable<T> elements, Func<T, int> phaseIndex, Func<T, bool> isEnclosed)
    {
        var byPhase = elements.GroupBy(phaseIndex).ToList();
        var latest = byPhase
            .Where(phase => phase.Any(isEnclosed))
            .OrderByDescending(phase => phase.Key)
            .FirstOrDefault();
        return latest?.ToList() ?? [];
    }
}
