namespace HVAKR.Api.Models;

public class MeResponse
{
    public MeUser? User { get; set; }
}

public class MeUser
{
    public string Id { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
