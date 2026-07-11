namespace HVAKR.Api.Updates;

public static class UpdaterCommand
{
    public static IReadOnlyList<string> BuildArguments(
        string installerPath,
        string version,
        string checksum,
        string statusPath,
        int currentRevitProcessId) =>
        [
            "--installer", installerPath,
            "--version", version,
            "--checksum", checksum,
            "--status", statusPath,
            "--revit-pid", currentRevitProcessId.ToString(),
        ];
}
