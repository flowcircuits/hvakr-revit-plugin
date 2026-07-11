using System.Net.Http.Headers;
using System.Text;
using HVAKR.Api.Models;
using Newtonsoft.Json;

namespace HVAKR.Api;

/// <summary>
/// Thin wrapper around the HVAKR REST API.
/// One instance per logged-in user — the API key is captured at construction.
/// </summary>
public sealed class Client(HttpClient httpClient, string apiKey)
{
    private const string ApiRoot = "https://api.hvakr.com";

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly string _apiKey = !string.IsNullOrWhiteSpace(apiKey)
        ? apiKey
        : throw new ArgumentException("API key is required.", nameof(apiKey));

    public async Task<List<ProjectSummary>> GetProjectsAsync()
    {
        var projects = new List<ProjectSummary>();
        string? cursor = null;

        do
        {
            var path = "/v0/projects?limit=100";
            if (cursor is not null)
            {
                path += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            using var response = await SendAsync(HttpMethod.Get, path).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var page = JsonConvert.DeserializeObject<ProjectListResponse>(content)
                ?? throw new JsonSerializationException("The project list response was empty.");
            if (page.Projects.Any(project => string.IsNullOrWhiteSpace(project.Id)))
            {
                throw new JsonSerializationException("A project list item is missing its id.");
            }

            projects.AddRange(page.Projects);
            if (!page.HasMore)
            {
                break;
            }

            cursor = !string.IsNullOrWhiteSpace(page.NextCursor)
                ? page.NextCursor
                : throw new JsonSerializationException("The project list response hasMore=true but no nextCursor.");
        }
        while (true);

        return projects;
    }

    public async Task<ProjectDetails?> GetProjectDetailsAsync(string projectId, bool expand = false)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Project ID is required.", nameof(projectId));

        var path = expand ? $"/v0/projects/{projectId}?expand=true" : $"/v0/projects/{projectId}";
        using var response = await SendAsync(HttpMethod.Get, path).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return JsonConvert.DeserializeObject<ProjectDetails>(content);
    }

    public Task<string> CreateProjectFromRevitAsync(string revitPayloadJson) =>
        SendRevitPayloadAsync(HttpMethod.Post, "/revit/v0/projects", revitPayloadJson);

    public Task<string> UpdateProjectFromRevitAsync(string projectId, string revitPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Project ID is required.", nameof(projectId));

        return SendRevitPayloadAsync(new HttpMethod("PATCH"), $"/revit/v0/projects/{projectId}", revitPayloadJson);
    }

    private async Task<string> SendRevitPayloadAsync(HttpMethod method, string path, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("Payload is required.", nameof(payloadJson));

        using var response = await SendAsync(method, path, new StringContent(payloadJson, Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, ApiRoot + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (content is not null)
        {
            request.Content = content;
        }

        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        // The HVAKR API holds the real logic, so its error body is the useful diagnostic
        // (validation messages, permission reasons). EnsureSuccessStatusCode would throw it away.
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body.Trim();
            throw new HttpRequestException(
                $"HVAKR API {method} {path} failed: {(int)response.StatusCode} {response.ReasonPhrase}. {detail}");
        }
    }
}
