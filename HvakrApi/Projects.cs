using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HvakrApi.Models;
using LoggerApi;

namespace HvakrApi
{
    public class HvakrApi
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public HvakrApi(HttpClient httpClient, string apiKey)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey;
        }

        public async Task<List<string>> GetProjectIdsAsync()
        {
            var requestUri = "https://api.hvakr.com/v0/projects";

            using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    var ids = new List<string>();
                    var json = JObject.Parse(content);

                    var token = json["ids"];
                    if (token == null || token.Type != JTokenType.Array)
                    {
                        Logger.LogError("No 'ids' array found in the response.");
                        return ids;
                    }

                    foreach (var id in token)
                    {
                        ids.Add(id.ToString());
                    }

                    return ids;
                }
            }
        }

        public async Task<ProjectDetails> GetProjectDetailsAsync(string projectId, bool expand = false)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                throw new ArgumentException("Project ID cannot be null or empty.", nameof(projectId));

            var requestUri = $"https://api.hvakr.com/v0/projects/{projectId}{(expand ? "?expand" : "")}";

            using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    // Logger.LogMessage($"Project Details Response: {content}");

                    var project = JsonConvert.DeserializeObject<ProjectDetails>(content);
                    return project;
                }
            }
        }

        public async Task<string> CreateProjectWithRevitPayloadAsync(string revitPayloadJson)
        {
            if (string.IsNullOrWhiteSpace(revitPayloadJson))
                throw new ArgumentException("Payload cannot be null or empty.", nameof(revitPayloadJson));

            var requestUri = "https://api.hvakr.com/v0/projects?revitPayload";

            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUri))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Content = new StringContent(revitPayloadJson, Encoding.UTF8, "application/json");

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        public async Task<string> UpdateProjectWithRevitPayloadAsync(string projectId, string revitPayloadJson)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                throw new ArgumentException("Project ID cannot be null or empty.", nameof(projectId));
            if (string.IsNullOrWhiteSpace(revitPayloadJson))
                throw new ArgumentException("Payload cannot be null or empty.", nameof(revitPayloadJson));

            var requestUri = $"https://api.hvakr.com/v0/projects/{projectId}?revitPayload";

            using (var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUri))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Content = new StringContent(revitPayloadJson, Encoding.UTF8, "application/json");

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
