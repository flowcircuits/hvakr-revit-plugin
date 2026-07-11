using System.Diagnostics;
using System.Security.Cryptography;
using HVAKR.Api.Updates;

namespace HVAKR.Revit.Updater;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = UpdaterOptions.Parse(args);
            return await new UpdaterRunner().RunAsync(options).ConfigureAwait(false);
        }
        catch
        {
            return 1;
        }
    }
}

public sealed record UpdaterOptions(
    string InstallerPath,
    string Version,
    string Checksum,
    string StatusPath,
    int RevitProcessId)
{
    public static UpdaterOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count || !args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Updater arguments must be --name value pairs.");
            values.Add(args[i], args[i + 1]);
        }

        var installer = Required("--installer");
        var version = Required("--version");
        var checksum = Required("--checksum");
        var status = Required("--status");
        var pid = int.Parse(Required("--revit-pid"));
        SemanticVersion.Parse(version);
        if (checksum.Length != 64 || checksum.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Checksum must be lowercase SHA-256 hexadecimal.");

        return new UpdaterOptions(installer, version, checksum, status, pid);

        string Required(string name) => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required updater argument {name}.");
    }
}

public sealed class UpdaterRunner
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IUpdateSignatureVerifier _signatureVerifier;
    private readonly Func<IReadOnlyList<int>> _getRevitProcessIds;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>> _runInstaller;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public UpdaterRunner(
        IUpdateSignatureVerifier? signatureVerifier = null,
        Func<IReadOnlyList<int>>? getRevitProcessIds = null,
        Func<ProcessStartInfo, CancellationToken, Task<int>>? runInstaller = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _signatureVerifier = signatureVerifier ?? new AuthenticodeVerifier();
        _getRevitProcessIds = getRevitProcessIds ?? GetRevitProcessIds;
        _runInstaller = runInstaller ?? RunInstallerAsync;
        _delay = delay ?? Task.Delay;
    }

    public async Task<int> RunAsync(UpdaterOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunCoreAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppendLog(options.StatusPath, $"Update failed: {ex}");
            new UpdateStateStore(Path.GetDirectoryName(options.StatusPath)!).SaveStatus(new UpdateStatus
            {
                Version = options.Version,
                Succeeded = false,
                Error = ex.Message,
                CompletedAt = DateTimeOffset.UtcNow,
            });
            throw;
        }
    }

    private async Task<int> RunCoreAsync(UpdaterOptions options, CancellationToken cancellationToken)
    {
        AppendLog(options.StatusPath, $"Preparing update {options.Version} from {options.InstallerPath}.");
        VerifyInstaller(options);
        await WaitForRevitToExitAsync(cancellationToken).ConfigureAwait(false);

        var startInfo = new ProcessStartInfo(options.InstallerPath)
        {
            UseShellExecute = true,
        };
        foreach (var argument in new[]
        {
            "/VERYSILENT",
            "/SUPPRESSMSGBOXES",
            "/NORESTART",
            "/NOCLOSEAPPLICATIONS",
            "/NORESTARTAPPLICATIONS",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        var exitCode = await _runInstaller(startInfo, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
            throw new InvalidOperationException($"The update installer exited with code {exitCode}.");

        var store = new UpdateStateStore(Path.GetDirectoryName(options.StatusPath)!);
        store.SaveStatus(new UpdateStatus
        {
            Version = options.Version,
            Succeeded = true,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        AppendLog(options.StatusPath, $"Update {options.Version} installed successfully.");

        var stagingDirectory = Path.GetDirectoryName(options.InstallerPath);
        if (stagingDirectory is not null && Directory.Exists(stagingDirectory))
            Directory.Delete(stagingDirectory, recursive: true);
        return 0;
    }

    public static void AppendLog(string statusPath, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(statusPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "updater.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging cannot interfere with installation or status reporting.
        }
    }

    private void VerifyInstaller(UpdaterOptions options)
    {
        if (!File.Exists(options.InstallerPath))
            throw new FileNotFoundException("The staged update installer is missing.", options.InstallerPath);

        using var stream = File.OpenRead(options.InstallerPath);
        var checksum = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(checksum, options.Checksum, StringComparison.Ordinal))
            throw new InvalidDataException("The staged update installer checksum is invalid.");
        _signatureVerifier.Verify(options.InstallerPath);
    }

    private async Task WaitForRevitToExitAsync(CancellationToken cancellationToken)
    {
        var consecutiveEmptyPolls = 0;
        while (consecutiveEmptyPolls < 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            consecutiveEmptyPolls = _getRevitProcessIds().Count == 0 ? consecutiveEmptyPolls + 1 : 0;

            if (consecutiveEmptyPolls < 2)
                await _delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<int> GetRevitProcessIds()
    {
        var processes = Process.GetProcessesByName("Revit");
        try
        {
            return processes.Select(process => process.Id).ToArray();
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static async Task<int> RunInstallerAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The update installer could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
