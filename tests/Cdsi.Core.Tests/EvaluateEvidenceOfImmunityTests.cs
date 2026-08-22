using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluateEvidenceOfImmunityTests
{
    // Real data: Measles - clinicalHistory guideline "020", dateOfBirth rule "01/01/1957" with
    // NO birthCountry restriction, exclusion "055" (Health care personnel). This is the spec's
    // own worked example antigen.
    private static readonly AntigenImmunityData MeaslesImmunity =
        AntigenSupportingDataLoader.LoadImmunityData(TestPaths.AntigenFile("AntigenSupportingData-_Measles-508.xml"));

    // Real data: Varicella - three clinicalHistory guidelines ("023","024","025"), dateOfBirth
    // rule "01/01/1980" WITH birthCountry "U.S." restriction, three exclusions ("055","007","003").
    private static readonly AntigenImmunityData VaricellaImmunity =
        AntigenSupportingDataLoader.LoadImmunityData(TestPaths.AntigenFile("AntigenSupportingData-_Varicella-508.xml"));

    private static Patient MakePatient(DateOnly dob, string? countryOfBirth = null, IReadOnlyList<PatientObservation>? observations = null) => new()
    {
        PatientId = "p1",
        DateOfBirth = dob,
        CountryOfBirth = countryOfBirth,
        ActiveObservations = observations ?? Array.Empty<PatientObservation>()
    };

    [Fact]
    public void ClinicalHistoryGuidelinePresent_IsImmune_RegardlessOfBirthDate()
    {
        // Rule 1: a documented clinical finding is sufficient on its own, even for a patient
        // born well after the birth-date presumption would apply.
        var patient = MakePatient(new DateOnly(2010, 1, 1), observations: new[]
        {
            new PatientObservation { Code = "020" }
        });

        Assert.True(EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, MeaslesImmunity));
    }

    [Fact]
    public void BornBeforeImmunityDate_NoExclusion_NoCountryRestriction_IsImmune()
    {
        // Measles' rule has no birthCountry restriction - Rule 3/unrestricted applies.
        var patient = MakePatient(new DateOnly(1950, 1, 1));

        Assert.True(EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, MeaslesImmunity));
    }

    [Fact]
    public void BornBeforeImmunityDate_WithExclusion_OverridesPresumption_NotImmune()
    {
        // Rule 2: the spec's own example - health care personnel don't get the presumption
        // despite birth year, since occupational exposure risk still applies.
        var patient = MakePatient(new DateOnly(1950, 1, 1), observations: new[]
        {
            new PatientObservation { Code = "055" }
        });

        Assert.False(EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, MeaslesImmunity));
    }

    [Fact]
    public void BornOnOrAfterImmunityDate_NotImmune()
    {
        // Rule 5: 1957-01-01 exactly - not strictly before the immunity birth date.
        var patient = MakePatient(new DateOnly(1957, 1, 1));

        Assert.False(EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, MeaslesImmunity));
    }

    [Fact]
    public void NoGuidelineAndNoQualifyingBirthDate_NotImmune()
    {
        var patient = MakePatient(new DateOnly(1990, 1, 1));

        Assert.False(EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, MeaslesImmunity));
    }

    [Fact]
    public void CountryRestrictedRule_MatchingCountry_NoExclusion_IsImmune()
    {
        // Varicella's rule requires birthCountry "U.S." - Rule 3.
        var patient = MakePatient(new DateOnly(1975, 1, 1), countryOfBirth: "U.S.");

        Assert.True(EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, VaricellaImmunity));
    }

    [Fact]
    public void CountryRestrictedRule_MismatchedCountry_NotImmune()
    {
        // Rule 4: born before the immunity date, no exclusion, but country doesn't match.
        var patient = MakePatient(new DateOnly(1975, 1, 1), countryOfBirth: "Canada");

        Assert.False(EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, VaricellaImmunity));
    }

    [Fact]
    public void CountryRestrictedRule_UnknownCountry_TreatedAsMismatch_NotImmune()
    {
        // No country of birth on file at all - can't confirm the match, so the country-restricted
        // presumption doesn't apply (a conservative, documented default rather than assuming a match).
        var patient = MakePatient(new DateOnly(1975, 1, 1), countryOfBirth: null);

        Assert.False(EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, VaricellaImmunity));
    }

    [Fact]
    public void AnyOfMultipleExclusions_OverridesPresumption()
    {
        // Varicella has 3 exclusion codes; "Pregnant" (007) alone should be enough to override,
        // same as "Health care personnel" was for Measles.
        var patient = MakePatient(new DateOnly(1975, 1, 1), countryOfBirth: "U.S.", observations: new[]
        {
            new PatientObservation { Code = "007" }
        });

        Assert.False(EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, VaricellaImmunity));
    }

    [Fact]
    public void AnyOfMultipleClinicalHistoryGuidelines_GrantsImmunity()
    {
        // Any one of Varicella's 3 guidelines should be sufficient, not just the first.
        var patient = MakePatient(new DateOnly(2015, 1, 1), observations: new[]
        {
            new PatientObservation { Code = "025" } // the third listed guideline
        });

        Assert.True(EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, VaricellaImmunity));
    }
}
