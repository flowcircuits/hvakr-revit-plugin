using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using HvakrApi;


namespace HvakrApi.Tests
{
    public class HvakrApiTests
    {
        private readonly string apiKey = "**REMOVED**"; // Replace with your own HVAKR API key
        [Fact]
        public async Task GetProjectsAsync_RealHttpCall_ReturnsProjects()
        {
            // Arrange
            using var httpClient = new System.Net.Http.HttpClient();
            var hvakrApi = new HvakrApi(httpClient, apiKey);

            // Act
            var projects = await hvakrApi.GetProjectIdsAsync();

            // Assert
            Assert.NotNull(projects);
            Assert.True((projects.Count > 0), "Expected at least one project to be returned.");
        }

        [Fact]
        public async Task GetProjectDetailsAsync_RealHttpCall_ReturnsExpectedProject()
        {
            using var httpClient = new System.Net.Http.HttpClient();
            var hvakrApi = new HvakrApi(httpClient, apiKey);

            var knownId = "**REMOVED**"; // Replace with your own stable project ID
            var project = await hvakrApi.GetProjectDetailsAsync(knownId);

            Assert.NotNull(project);
            Assert.Equal(knownId, project.Id);
            Assert.False(string.IsNullOrWhiteSpace(project.Name), "Project name should not be empty.");
            Assert.NotEqual(0, project.Latitude);
            Assert.NotEqual(0, project.Longitude);
        }
    }
}
