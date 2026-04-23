namespace HVAKR.Api.Models;

public class ProjectDetails
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Owner { get; set; }
    public string? ConstructionType { get; set; }
    public BuildingInfo? Building { get; set; }
    public bool IsTemplate { get; set; }
    public double Elevation { get; set; }
    public WeatherSpec? WeatherSpec { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? LastOpenTime { get; set; }
    public Dictionary<string, UserProfile>? Users { get; set; }

    // Populated only when fetched with ?expand=true.
    public Dictionary<string, SpaceDetails>? Spaces { get; set; }
}

public class BuildingInfo
{
    public string? AshraeBuildingTypeId { get; set; }
}

public class Timestamp
{
    public long Seconds { get; set; }
    public int Nanoseconds { get; set; }
}

public class WeatherSpec
{
    public string? CoolPercent { get; set; }
    public string? HeatPercent { get; set; }
    public string? SelectedStationId { get; set; }
    public List<string>? NearestWeatherStationIds { get; set; }
    public bool Loading { get; set; }
}

public class UserProfile
{
    public int Role { get; set; }
    public string? ProfilePicture { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public UserStatus? Status { get; set; }
}

public class UserStatus
{
    public string? CurrentProjectId { get; set; }
    public Timestamp? LastChanged { get; set; }
    public bool Online { get; set; }
}
