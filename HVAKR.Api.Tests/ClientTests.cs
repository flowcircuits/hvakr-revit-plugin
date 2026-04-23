using Xunit;

namespace HVAKR.Api.Tests;

/// <summary>
/// Integration tests — these hit the real HVAKR API.
/// Set HVAKR_ACCESS_TOKEN (and optionally HVAKR_TEST_PROJECT_ID) to run them.
/// Without the env vars the tests no-op so CI stays green.
/// </summary>
public class ClientTests
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("HVAKR_ACCESS_TOKEN");
    private static string? KnownProjectId => Environment.GetEnvironmentVariable("HVAKR_TEST_PROJECT_ID");

    [Fact]
    public async Task GetProjectIdsAsync_returns_at_least_one_project()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return; // env var not set; skip silently

        using var http = new HttpClient();
        var client = new Client(http, ApiKey);

        var ids = await client.GetProjectIdsAsync();

        Assert.NotNull(ids);
        Assert.NotEmpty(ids);
    }

    [Fact]
    public async Task GetProjectDetailsAsync_returns_expected_project()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(KnownProjectId))
            return;

        using var http = new HttpClient();
        var client = new Client(http, ApiKey);

        var project = await client.GetProjectDetailsAsync(KnownProjectId);

        Assert.NotNull(project);
        Assert.Equal(KnownProjectId, project!.Id);
        Assert.False(string.IsNullOrWhiteSpace(project.Name), "Project name should not be empty.");
        Assert.NotEqual(0, project.Latitude);
        Assert.NotEqual(0, project.Longitude);
    }
}
