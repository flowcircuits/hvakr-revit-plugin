using Xunit;

namespace HVAKR.Api.Tests;

/// <summary>
/// DPAPI exists only on Windows, so these tests no-op elsewhere. CI runs them on Windows.
/// </summary>
public sealed class CredentialStoreTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), "HVAKR.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory)) Directory.Delete(_rootDirectory, true);
    }

    [Fact]
    public void Saved_key_loads_back_and_is_not_stored_as_plain_text()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new CredentialStore(_rootDirectory);

        store.Save("hvakr-test-key");

        Assert.Equal("hvakr-test-key", new CredentialStore(_rootDirectory).Load());
        Assert.DoesNotContain("hvakr-test-key", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Load_returns_null_when_nothing_is_saved()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Null(new CredentialStore(_rootDirectory).Load());
    }

    [Fact]
    public void Clear_removes_the_saved_key()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new CredentialStore(_rootDirectory);
        store.Save("hvakr-test-key");

        store.Clear();

        Assert.False(File.Exists(store.FilePath));
        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_drops_a_file_it_cannot_decrypt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new CredentialStore(_rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
        File.WriteAllText(store.FilePath, "not a DPAPI blob");

        Assert.Null(store.Load());
        Assert.False(File.Exists(store.FilePath));
    }
}
