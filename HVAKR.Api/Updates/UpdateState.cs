using Newtonsoft.Json;

namespace HVAKR.Api.Updates;

public sealed class UpdateState
{
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? ReadyVersion { get; set; }
    public string? ReadyInstallerPath { get; set; }
    public string? ReadySha256 { get; set; }
    public string? ReleaseNotesUrl { get; set; }
    public string? DismissedVersion { get; set; }
}

public sealed class UpdateStatus
{
    public string Version { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}

public sealed class UpdateStateStore(string rootDirectory)
{
    public string StatePath { get; } = Path.Combine(rootDirectory, "update-state.json");
    public string StatusPath { get; } = Path.Combine(rootDirectory, "update-status.json");

    public UpdateState LoadState()
    {
        try
        {
            return Load<UpdateState>(StatePath) ?? new UpdateState();
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to load update state", ex);
            return new UpdateState();
        }
    }

    public UpdateStatus? LoadStatus()
    {
        try
        {
            return Load<UpdateStatus>(StatusPath);
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to load update status", ex);
            return null;
        }
    }

    public void SaveState(UpdateState state) => WriteAtomic(StatePath, state);

    public void SaveStatus(UpdateStatus status) => WriteAtomic(StatusPath, status);

    public void ClearStatus()
    {
        if (File.Exists(StatusPath)) File.Delete(StatusPath);
    }

    private static T? Load<T>(string path)
    {
        if (!File.Exists(path)) return default;
        return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
    }

    private static void WriteAtomic(string path, object value)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(value, Formatting.Indented));
        File.Move(temporaryPath, path, true);
    }
}
