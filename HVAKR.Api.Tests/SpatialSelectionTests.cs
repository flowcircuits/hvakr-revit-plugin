using Xunit;

namespace HVAKR.Api.Tests;

public class SpatialSelectionTests
{
    private sealed record Element(string Id, int Phase, double Area);

    private static List<string> Select(params Element[] elements) =>
        SpatialSelection.InLatestEnclosedPhase(elements, e => e.Phase, e => e.Area > 0).Select(e => e.Id).ToList();

    [Fact]
    public void Exports_only_the_latest_phase_so_a_room_is_not_doubled()
    {
        // The same room in Existing (phase 0) and New Construction (phase 1) is two Revit elements.
        Assert.Equal(
            new[] { "office-new", "lobby-new" },
            Select(new("office-existing", 0, 200), new("office-new", 1, 200), new("lobby-new", 1, 500)));
    }

    [Fact]
    public void Skips_a_later_phase_that_has_no_enclosed_element()
    {
        Assert.Equal(
            new[] { "office-existing" },
            Select(new("office-existing", 0, 200), new("demolished", 2, 0)));
    }

    [Fact]
    public void Keeps_not_enclosed_elements_of_the_selected_phase()
    {
        Assert.Equal(new[] { "office", "open-area" }, Select(new("office", 1, 200), new("open-area", 1, 0)));
    }

    [Fact]
    public void Returns_nothing_when_no_element_is_enclosed_so_the_export_falls_back()
    {
        Assert.Empty(Select(new Element("stray-space", 1, 0)));
        Assert.Empty(Select());
    }

    [Fact]
    public void Elements_without_a_known_phase_rank_below_known_phases()
    {
        Assert.Equal(new[] { "phased" }, Select(new("unknown", -1, 100), new("phased", 0, 100)));
        Assert.Equal(new[] { "unknown" }, Select(new Element("unknown", -1, 100)));
    }
}
