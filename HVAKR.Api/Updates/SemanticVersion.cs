using System.Text.RegularExpressions;

namespace HVAKR.Api.Updates;

public readonly partial record struct SemanticVersion(int Major, int Minor, int Patch) : IComparable<SemanticVersion>
{
    public static SemanticVersion Parse(string value)
    {
        var match = VersionPattern().Match(value);
        if (!match.Success)
        {
            throw new FormatException($"'{value}' is not a strict major.minor.patch version.");
        }

        return new SemanticVersion(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value));
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        try
        {
            version = Parse(value ?? string.Empty);
            return true;
        }
        catch (FormatException)
        {
            version = default;
            return false;
        }
        catch (OverflowException)
        {
            version = default;
            return false;
        }
    }

    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
