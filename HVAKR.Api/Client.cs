using System.Net.Http.Headers;
using System.Text;
using HVAKR.Api.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HVAKR.Api;

/// <summary>
/// Thin wrapper around the HVAKR v0 REST API.
/// One instance per logged-in user — the API key is captured at construction.
/// </summary>
public sealed class Client(HttpClient httpClient, string apiKey)
{
    private const string BaseUrl = "https://api.hvakr.com/v0";

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly string _apiKey = !string.IsNullOrWhiteSpace(apiKey)
        ? apiKey
        : throw new ArgumentException("API key is required.", nameof(apiKey));

    public async Task<List<string>> GetProjectIdsAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, "/projects").ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var token = JObject.Parse(content)["ids"];
        if (token is not JArray array)
        {
            Logger.LogError("No 'ids' array found in /projects response.");
            return [];
        }

        return array.Select(id => id.ToString()).ToList();
    }

    public async Task<ProjectDetails?> GetProjectDetailsAsync(string projectId, bool expand = false)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Project ID is required.", nameof(projectId));

        var path = expand ? $"/projects/{projectId}?expand=true" : $"/projects/{projectId}";
        using var response = await SendAsync(HttpMethod.Get, path).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return JsonConvert.DeserializeObject<ProjectDetails>(content);
    }

    public Task<string> CreateProjectFromRevitAsync(string revitPayloadJson) =>
        SendRevitPayloadAsync(HttpMethod.Post, "/projects?revitPayload", revitPayloadJson);

    public Task<string> UpdateProjectFromRevitAsync(string projectId, string revitPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Project ID is required.", nameof(projectId));

        return SendRevitPayloadAsync(new HttpMethod("PATCH"), $"/projects/{projectId}?revitPayload", revitPayloadJson);
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
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (content is not null)
        {
            request.Content = content;
        }

        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return response;
    }
}
