using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace HVAKR.Api.Updates;

public enum UpdateCheckOutcome
{
    Throttled,
    Current,
    Ready,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, UpdateState State, string? Error = null);

public sealed class UpdateService
{
    public const string ManifestUrl = "https://storage.googleapis.com/hvakr-desktop-releases/revit/latest.json";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly IUpdateSignatureVerifier _signatureVerifier;
    private readonly Func<DateTimeOffset> _now;
    private readonly UpdateStateStore _store;
    private readonly string _rootDirectory;
    private readonly string _installedUpdaterPath;
    private readonly SemanticVersion? _currentVersion;

    public UpdateService(
        HttpClient? httpClient = null,
        IUpdateSignatureVerifier? signatureVerifier = null,
        Func<DateTimeOffset>? now = null,
        string? rootDirectory = null,
        string? installedUpdaterPath = null,
        SemanticVersion? currentVersion = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _signatureVerifier = signatureVerifier ?? new AuthenticodeVerifier();
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HVAKR",
            "Revit Plugin");
        _installedUpdaterPath = installedUpdaterPath ?? Path.Combine(AppContext.BaseDirectory, "HVAKR.Revit.Updater.exe");
        _currentVersion = currentVersion;
        _store = new UpdateStateStore(_rootDirectory);
    }

    public SemanticVersion CurrentVersion
    {
        get
        {
            if (_currentVersion is { } currentVersion) return currentVersion;
            var informational = typeof(UpdateService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                .Split('+')[0];
            return SemanticVersion.TryParse(informational, out var version) ? version : default;
        }
    }

    public UpdateState LoadState() => _store.LoadState();

    public UpdateStatus? LoadStatus() => _store.LoadStatus();

    public void ClearStatus()
    {
        try
        {
            _store.ClearStatus();
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to clear update status", ex);
        }
    }

    public void AcknowledgeSuccessfulUpdate(UpdateStatus status)
    {
        try
        {
            var state = _store.LoadState();
            if (status.Succeeded && state.ReadyVersion == status.Version)
            {
                ClearReadyState(state);
                _store.SaveState(state);
            }
            _store.ClearStatus();
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to clear successful update status", ex);
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(bool force, CancellationToken cancellationToken = default)
    {
        var state = _store.LoadState();
        var now = _now();
        if (!force && state.LastCheckedAt is { } lastChecked && now - lastChecked < CheckInterval)
            return new UpdateCheckResult(UpdateCheckOutcome.Throttled, state);

        state.LastCheckedAt = now;
        try
        {
            _store.SaveState(state);
            var json = await _httpClient.GetStringAsync(ManifestUrl, cancellationToken).ConfigureAwait(false);
            var manifest = UpdateManifest.Parse(json);
            var availableVersion = manifest.Validate();
            if (availableVersion <= CurrentVersion)
            {
                ClearReadyState(state);
                _store.SaveState(state);
                return new UpdateCheckResult(UpdateCheckOutcome.Current, state);
            }

            var stagingDirectory = Path.Combine(_rootDirectory, "staging", manifest.Version);
            Directory.CreateDirectory(stagingDirectory);
            var installerPath = Path.Combine(stagingDirectory, $"HVAKR-Revit-Plugin-{manifest.Version}.exe");
            await DownloadAndVerifyAsync(manifest, installerPath, cancellationToken).ConfigureAwait(false);

            state.ReadyVersion = manifest.Version;
            state.ReadyInstallerPath = installerPath;
            state.ReadySha256 = manifest.Installer.Sha256;
            state.ReleaseNotesUrl = manifest.ReleaseNotesUrl;
            if (force) state.DismissedVersion = null;
            _store.SaveState(state);
            return new UpdateCheckResult(UpdateCheckOutcome.Ready, state);
        }
        catch (Exception ex)
        {
            Logger.LogError("Update check failed", ex);
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, state, ex.Message);
        }
    }

    public void DismissReadyUpdate()
    {
        try
        {
            var state = _store.LoadState();
            state.DismissedVersion = state.ReadyVersion;
            _store.SaveState(state);
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to save dismissed update", ex);
        }
    }

    public void LaunchUpdater(int currentRevitProcessId)
    {
        var state = _store.LoadState();
        if (state.ReadyVersion is null || state.ReadyInstallerPath is null || state.ReadySha256 is null)
            throw new InvalidOperationException("No verified update is ready to install.");
        if (!File.Exists(_installedUpdaterPath))
            throw new FileNotFoundException("The HVAKR updater is not installed.", _installedUpdaterPath);

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "HVAKR", "Updater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var updaterPath = Path.Combine(temporaryDirectory, "HVAKR.Revit.Updater.exe");
        File.Copy(_installedUpdaterPath, updaterPath);

        var startInfo = new ProcessStartInfo(updaterPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = temporaryDirectory,
        };
        foreach (var argument in UpdaterCommand.BuildArguments(
            state.ReadyInstallerPath,
            state.ReadyVersion,
            state.ReadySha256,
            _store.StatusPath,
            currentRevitProcessId))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo) ?? throw new InvalidOperationException("The HVAKR updater could not be started.");
    }

    private async Task DownloadAndVerifyAsync(
        UpdateManifest manifest,
        string installerPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(installerPath))
        {
            try
            {
                VerifyInstaller(installerPath, manifest.Installer);
                return;
            }
            catch (Exception)
            {
                File.Delete(installerPath);
            }
        }

        var temporaryPath = installerPath + ".download";
        try
        {
            using var response = await _httpClient.GetAsync(
                manifest.Installer.Url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

            VerifyInstaller(temporaryPath, manifest.Installer);
            File.Move(temporaryPath, installerPath, true);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    private void VerifyInstaller(string path, UpdateInstaller installer)
    {
        var info = new FileInfo(path);
        if (info.Length != installer.Size)
            throw new InvalidDataException($"The update installer size was {info.Length}, expected {installer.Size}.");

        using var stream = File.OpenRead(path);
        var checksum = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(checksum, installer.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("The update installer checksum did not match the manifest.");

        _signatureVerifier.Verify(path);
    }

    private static void ClearReadyState(UpdateState state)
    {
        state.ReadyVersion = null;
        state.ReadyInstallerPath = null;
        state.ReadySha256 = null;
        state.ReleaseNotesUrl = null;
        state.DismissedVersion = null;
    }
}
