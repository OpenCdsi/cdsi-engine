namespace Cdsi.Core.Models;

/// <summary>Per Table 5-2, "Assumed Value if Empty" for patient gender is Unknown — never default to a specific gender.</summary>
public enum Gender
{
    Unknown,
    Male,
    Female
}
