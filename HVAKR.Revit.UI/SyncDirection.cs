namespace HVAKR.Revit.UI;

/// <summary>Which way the Revit → HVAKR export should land.</summary>
public enum SyncDirection
{
    /// <summary>Create a new HVAKR project from the current Revit model.</summary>
    Create,

    /// <summary>Update the selected HVAKR project with the current Revit model.</summary>
    Update,
}
