using Newtonsoft.Json;

namespace HVAKR.Api.Updates;

public sealed class UpdateManifest
{
    public int SchemaVersion { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public UpdateInstaller Installer { get; set; } = new();
    public string ReleaseNotesUrl { get; set; } = string.Empty;

    public SemanticVersion Validate()
    {
        if (SchemaVersion != 1)
            throw new JsonSerializationException($"Unsupported update manifest schema {SchemaVersion}.");

        var version = SemanticVersion.Parse(Version);
        if (PublishedAt == default)
            throw new JsonSerializationException("The manifest publishedAt value is required.");
        if (!Uri.TryCreate(Installer.Url, UriKind.Absolute, out var installerUri) || installerUri.Scheme != Uri.UriSchemeHttps)
            throw new JsonSerializationException("The installer URL must use HTTPS.");
        if (Installer.Size <= 0)
            throw new JsonSerializationException("The installer size must be positive.");
        if (Installer.Sha256.Length != 64 || Installer.Sha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new JsonSerializationException("The installer SHA-256 must be 64 lowercase hexadecimal characters.");
        if (!Uri.TryCreate(ReleaseNotesUrl, UriKind.Absolute, out var notesUri) || notesUri.Scheme != Uri.UriSchemeHttps)
            throw new JsonSerializationException("The release notes URL must use HTTPS.");

        return version;
    }

    public static UpdateManifest Parse(string json)
    {
        var manifest = JsonConvert.DeserializeObject<UpdateManifest>(json)
            ?? throw new JsonSerializationException("The update manifest was empty.");
        manifest.Validate();
        return manifest;
    }
}

public sealed class UpdateInstaller
{
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
}
