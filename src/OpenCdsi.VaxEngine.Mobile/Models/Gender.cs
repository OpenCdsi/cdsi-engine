namespace OpenCdsi.VaxEngine.Mobile.Models;

// Named "Gender" rather than "Sex" to match the field name used in the
// vaxengine.core spec, even though CDSi evaluation logic reasons about
// biological sex for a handful of antigen rules. Keep this in mind if/when
// this value gets mapped onto the engine's actual input DTO.
public enum Gender
{
    Male,
    Female,
    Unknown
}
