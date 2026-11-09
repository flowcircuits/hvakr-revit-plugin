using System.Net;

namespace HVAKR.Api;

public enum LoginFailure
{
    /// <summary>Network or server failure. The key may still be good.</summary>
    Unavailable,

    /// <summary>401: the API does not accept the key. A saved key must be deleted.</summary>
    InvalidKey,

    /// <summary>403: the key is valid, but the account has no license or its plan has no API access.</summary>
    NoApiAccess,
}

public static class LoginFailures
{
    public static LoginFailure Classify(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => LoginFailure.InvalidKey,
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => LoginFailure.NoApiAccess,
        _ => LoginFailure.Unavailable,
    };
}
