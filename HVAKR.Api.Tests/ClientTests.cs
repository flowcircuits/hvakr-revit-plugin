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
    public async Task GetProjectsAsync_returns_at_least_one_project()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return; // env var not set; skip silently

        using var http = new HttpClient();
        var client = new Client(http, ApiKey);

        var projects = await client.GetProjectsAsync();

        Assert.NotNull(projects);
        Assert.NotEmpty(projects);
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

    [Fact]
    public async Task GetProjectsAsync_follows_the_paginated_project_summary_contract()
    {
        var handler = new StubHttpMessageHandler(
            """{"projects":[{"id":"project-a","name":"Alpha"}],"hasMore":true,"nextCursor":"project-a"}""",
            """{"projects":[{"id":"project-b","name":"Beta"}],"hasMore":false,"nextCursor":null}""");
        using var http = new HttpClient(handler);
        var client = new Client(http, "test-token");

        var projects = await client.GetProjectsAsync();

        Assert.Equal(new[] { "project-a", "project-b" }, projects.Select(project => project.Id));
        Assert.Equal(
            new[]
            {
                "https://api.hvakr.com/v0/projects?limit=100",
                "https://api.hvakr.com/v0/projects?limit=100&cursor=project-a",
            },
            handler.Requests.Select(request => request.RequestUri!.ToString()));
    }

    [Fact]
    public async Task Revit_writes_use_the_versioned_ingestion_namespace()
    {
        var handler = new StubHttpMessageHandler("""{"id":"created"}""", """{"id":"project-a"}""");
        using var http = new HttpClient(handler);
        var client = new Client(http, "test-token");

        await client.CreateProjectFromRevitAsync("{}");
        await client.UpdateProjectFromRevitAsync("project-a", "{}");

        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://api.hvakr.com/revit/v0/projects", request.RequestUri!.ToString());
            },
            request =>
            {
                Assert.Equal(HttpMethod.Patch, request.Method);
                Assert.Equal("https://api.hvakr.com/revit/v0/projects/project-a", request.RequestUri!.ToString());
            });
    }

    private sealed class StubHttpMessageHandler(params string[] responseBodies) : HttpMessageHandler
    {
        private readonly Queue<string> _responseBodies = new(responseBodies);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBodies.Dequeue()),
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var request in Requests)
                {
                    request.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
