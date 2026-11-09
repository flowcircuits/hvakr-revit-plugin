using System.Net;
using Xunit;

namespace HVAKR.Api.Tests;

public class LoginFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LoginFailure.InvalidKey)]
    [InlineData(HttpStatusCode.Forbidden, LoginFailure.NoApiAccess)]
    [InlineData(HttpStatusCode.InternalServerError, LoginFailure.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, LoginFailure.Unavailable)]
    public void Classify_maps_the_HTTP_status(HttpStatusCode statusCode, LoginFailure expected) =>
        Assert.Equal(expected, LoginFailures.Classify(new HttpRequestException("failed", null, statusCode)));

    [Fact]
    public void Classify_treats_a_network_error_as_unavailable()
    {
        Assert.Equal(LoginFailure.Unavailable, LoginFailures.Classify(new HttpRequestException("No such host")));
        Assert.Equal(LoginFailure.Unavailable, LoginFailures.Classify(new TaskCanceledException()));
    }
}
