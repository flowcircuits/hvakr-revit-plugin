using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using HVAKR.Api.Updates;
using HVAKR.Revit.Updater;
using Newtonsoft.Json;

namespace HVAKR.Api.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HVAKR.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("0.1.0", "0.1.1", -1)]
    [InlineData("1.9.9", "2.0.0", -1)]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("3.0.0", "2.99.99", 1)]
    public void Semantic_versions_compare_by_major_minor_patch(string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(SemanticVersion.Parse(left).CompareTo(SemanticVersion.Parse(right))));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-beta")]
    public void Semantic_versions_reject_non_strict_values(string value)
    {
        Assert.Throws<FormatException>(() => SemanticVersion.Parse(value));
    }

    [Fact]
    public void Manifest_parser_validates_the_public_contract()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var manifest = UpdateManifest.Parse(CreateManifest(bytes));

        Assert.Equal("0.1.0", manifest.Version);
        Assert.Equal(3, manifest.Installer.Size);
    }

    [Fact]
    public void Manifest_parser_rejects_malformed_metadata()
    {
        var json = CreateManifest([1, 2, 3]).Replace("\"schemaVersion\":1", "\"schemaVersion\":2");

        Assert.Throws<JsonSerializationException>(() => UpdateManifest.Parse(json));
    }

    [Fact]
    public async Task Automatic_checks_are_throttled_for_24_hours()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var http = new HttpClient(handler);
        var store = new UpdateStateStore(_root);
        store.SaveState(new UpdateState { LastCheckedAt = now.AddHours(-23) });
        var service = new UpdateService(http, new AcceptingVerifier(), () => now, _root, "updater.exe");

        var result = await service.CheckAsync(force: false);

        Assert.Equal(UpdateCheckOutcome.Throttled, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Newer_update_is_downloaded_verified_and_persisted()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var handler = new QueueHandler(
            JsonResponse(CreateManifest(bytes)),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var http = new HttpClient(handler);
        var verifier = new AcceptingVerifier();
        var service = new UpdateService(
            http,
            verifier,
            rootDirectory: _root,
            installedUpdaterPath: "updater.exe",
            currentVersion: new SemanticVersion(0, 0, 0));

        var result = await service.CheckAsync(force: true);

        Assert.Equal(UpdateCheckOutcome.Ready, result.Outcome);
        Assert.Equal("0.1.0", result.State.ReadyVersion);
        Assert.True(File.Exists(result.State.ReadyInstallerPath));
        Assert.Equal(result.State.ReadyInstallerPath, verifier.VerifiedPath);
        Assert.Equal("0.1.0", new UpdateStateStore(_root).LoadState().ReadyVersion);
    }

    [Fact]
    public async Task Hash_mismatch_is_rejected_without_leaving_partial_download()
    {
        var expected = new byte[] { 1, 2, 3 };
        var actual = new byte[] { 3, 2, 1 };
        var handler = new QueueHandler(
            JsonResponse(CreateManifest(expected)),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(actual) });
        using var http = new HttpClient(handler);
        var service = new UpdateService(
            http,
            new AcceptingVerifier(),
            rootDirectory: _root,
            installedUpdaterPath: "updater.exe",
            currentVersion: new SemanticVersion(0, 0, 0));

        var result = await service.CheckAsync(force: true);

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFiles(_root, "*.download", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Rejected_Authenticode_signature_is_not_staged()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var handler = new QueueHandler(
            JsonResponse(CreateManifest(bytes)),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var http = new HttpClient(handler);
        var service = new UpdateService(
            http,
            new RejectingVerifier(),
            rootDirectory: _root,
            installedUpdaterPath: "updater.exe",
            currentVersion: new SemanticVersion(0, 0, 0));

        var result = await service.CheckAsync(force: true);

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
        Assert.Null(result.State.ReadyVersion);
        Assert.Empty(Directory.GetFiles(_root, "*.exe", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.1.0")]
    public async Task Equal_or_older_manifest_does_not_download(string currentVersion)
    {
        var handler = new QueueHandler(JsonResponse(CreateManifest([1, 2, 3])));
        using var http = new HttpClient(handler);
        var service = new UpdateService(
            http,
            new AcceptingVerifier(),
            rootDirectory: _root,
            installedUpdaterPath: "updater.exe",
            currentVersion: SemanticVersion.Parse(currentVersion));

        var result = await service.CheckAsync(force: true);

        Assert.Equal(UpdateCheckOutcome.Current, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Updater_arguments_round_trip_paths_with_spaces()
    {
        var args = UpdaterCommand.BuildArguments(
            @"C:\staged files\installer.exe",
            "0.1.0",
            new string('a', 64),
            @"C:\status files\status.json",
            1234);

        var options = UpdaterOptions.Parse(args);

        Assert.Equal(@"C:\staged files\installer.exe", options.InstallerPath);
        Assert.Equal(@"C:\status files\status.json", options.StatusPath);
        Assert.Equal(1234, options.RevitProcessId);
    }

    [Fact]
    public void State_and_status_writes_replace_files_atomically()
    {
        var store = new UpdateStateStore(_root);
        store.SaveState(new UpdateState { ReadyVersion = "0.1.0" });
        store.SaveState(new UpdateState { ReadyVersion = "0.1.1" });
        store.SaveStatus(new UpdateStatus { Version = "0.1.1", Succeeded = false, Error = "installer failed" });

        Assert.Equal("0.1.1", store.LoadState().ReadyVersion);
        Assert.Equal("installer failed", store.LoadStatus()!.Error);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Updater_waits_for_existing_and_new_Revit_processes_then_cleans_up()
    {
        var staging = Path.Combine(_root, "staging", "0.1.0");
        Directory.CreateDirectory(staging);
        var installer = Path.Combine(staging, "installer.exe");
        var bytes = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(installer, bytes);
        var processPolls = new Queue<IReadOnlyList<int>>(
        [
            new[] { 10 },
            new[] { 10, 11 },
            Array.Empty<int>(),
            Array.Empty<int>(),
        ]);
        ProcessStartInfo? capturedStartInfo = null;
        var runner = new UpdaterRunner(
            new AcceptingVerifier(),
            () => processPolls.Dequeue(),
            (startInfo, _) =>
            {
                capturedStartInfo = startInfo;
                return Task.FromResult(0);
            },
            (_, _) => Task.CompletedTask);
        var options = new UpdaterOptions(
            installer,
            "0.1.0",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Path.Combine(_root, "update-status.json"),
            10);

        var exitCode = await runner.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(processPolls);
        Assert.True(capturedStartInfo!.UseShellExecute);
        Assert.Contains("/NOCLOSEAPPLICATIONS", capturedStartInfo!.ArgumentList);
        Assert.False(Directory.Exists(staging));
        Assert.True(new UpdateStateStore(_root).LoadStatus()!.Succeeded);
    }

    [Fact]
    public async Task Updater_retains_staging_and_writes_failure_status_for_retry()
    {
        var staging = Path.Combine(_root, "staging", "0.1.0");
        Directory.CreateDirectory(staging);
        var installer = Path.Combine(staging, "installer.exe");
        var bytes = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(installer, bytes);
        var runner = new UpdaterRunner(
            new AcceptingVerifier(),
            () => Array.Empty<int>(),
            (_, _) => Task.FromResult(7),
            (_, _) => Task.CompletedTask);
        var options = new UpdaterOptions(
            installer,
            "0.1.0",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Path.Combine(_root, "update-status.json"),
            10);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(options));

        Assert.Contains("code 7", error.Message);
        Assert.True(File.Exists(installer));
        var status = new UpdateStateStore(_root).LoadStatus()!;
        Assert.False(status.Succeeded);
        Assert.Contains("code 7", status.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json),
    };

    private static string CreateManifest(byte[] installer)
    {
        var hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        return $$"""
            {"schemaVersion":1,"version":"0.1.0","publishedAt":"2026-07-10T00:00:00Z","installer":{"url":"https://example.com/HVAKR-Revit-Plugin-0.1.0.exe","sha256":"{{hash}}","size":{{installer.Length}}},"releaseNotesUrl":"https://github.com/flowcircuits/hvakr-revit-plugin/releases/tag/v0.1.0"}
            """;
    }

    private sealed class AcceptingVerifier : IUpdateSignatureVerifier
    {
        public string? VerifiedPath { get; private set; }
        public void Verify(string path) => VerifiedPath = path;
    }

    private sealed class RejectingVerifier : IUpdateSignatureVerifier
    {
        public void Verify(string path) => throw new InvalidDataException("Untrusted publisher.");
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
