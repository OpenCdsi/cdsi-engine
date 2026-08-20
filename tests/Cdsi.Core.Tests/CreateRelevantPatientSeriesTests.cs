using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class CreateRelevantPatientSeriesTests
{
    private static readonly IReadOnlyList<AntigenSeries> HibSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Hib-508.xml"));

    private static readonly IReadOnlyList<AntigenSeries> HpvSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HPV-508.xml"));

    // Real series from the Hib file: "Hib risk child 2-dose series", Risk type,
    // indication observationCode 146, beginAge 12 months, endAge 5 years.
    private const string HibRiskSeriesName = "Hib risk child 2-dose series";
    private const string HibRiskObservationCode = "146";

    private static Patient MakePatient(DateOnly dob, Gender gender = Gender.Unknown,
        IReadOnlyList<PatientObservation>? activeObservations = null,
        IReadOnlyList<string>? unresolvedObservationCodes = null) => new()
    {
        PatientId = "test-patient",
        DateOfBirth = dob,
        Gender = gender,
        ActiveObservations = activeObservations ?? Array.Empty<PatientObservation>(),
        UnresolvedObservationCodes = unresolvedObservationCodes ?? Array.Empty<string>()
    };

    [Fact]
    public void StandardSeries_IsAlwaysRelevant_RegardlessOfObservations()
    {
        var patient = MakePatient(new DateOnly(2020, 1, 1));
        var assessmentDate = new DateOnly(2020, 3, 1);

        var result = CreateRelevantPatientSeries.Execute(patient, HibSeries, assessmentDate);

        Assert.Contains(result.RelevantSeries, s => s.SeriesType == SeriesType.Standard);
    }

    [Fact]
    public void RiskSeries_NotRelevant_WhenIndicationObservationDefinitivelyAbsent()
    {
        // Patient is in the indication's age window (12mo-5y) but has no matching observation,
        // and hasn't been flagged as unresolved either -> definitively "No" (Table 5-4 Rule 2).
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);
        var assessmentDate = new DateOnly(2022, 6, 1); // age ~2.4 years

        var result = CreateRelevantPatientSeries.Execute(patient, HibSeries, assessmentDate);

        Assert.DoesNotContain(result.RelevantSeries, s => s.SeriesName == HibRiskSeriesName);
        Assert.Empty(result.UnresolvedIndications);
    }

    [Fact]
    public void RiskSeries_Relevant_WhenObservationPresentAndWithinAgeWindow()
    {
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob, activeObservations: new[]
        {
            new PatientObservation { Code = HibRiskObservationCode, Text = "B-lymphocyte deficiency" }
        });
        var assessmentDate = new DateOnly(2022, 6, 1); // age ~2.4 years, within [1y, 5y)

        var result = CreateRelevantPatientSeries.Execute(patient, HibSeries, assessmentDate);

        Assert.Contains(result.RelevantSeries, s => s.SeriesName == HibRiskSeriesName);
    }

    [Fact]
    public void RiskSeries_NotRelevant_WhenObservationPresentButOutsideAgeWindow()
    {
        // Table 5-4 Rule 4: age window failing overrides an otherwise-matching observation.
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob, activeObservations: new[]
        {
            new PatientObservation { Code = HibRiskObservationCode }
        });
        var assessmentDate = new DateOnly(2032, 1, 1); // age 12 - outside the 12mo-5y window

        var result = CreateRelevantPatientSeries.Execute(patient, HibSeries, assessmentDate);

        Assert.DoesNotContain(result.RelevantSeries, s => s.SeriesName == HibRiskSeriesName);
    }

    [Fact]
    public void RiskSeries_ProducesClinicianNotification_WhenObservationIsUnresolved()
    {
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob, unresolvedObservationCodes: new[] { HibRiskObservationCode });
        var assessmentDate = new DateOnly(2022, 6, 1); // within age window

        var result = CreateRelevantPatientSeries.Execute(patient, HibSeries, assessmentDate);

        Assert.DoesNotContain(result.RelevantSeries, s => s.SeriesName == HibRiskSeriesName);
        Assert.Contains(result.UnresolvedIndications, n => n.SeriesName == HibRiskSeriesName && n.ObservationCode == HibRiskObservationCode);
    }

    [Theory]
    [InlineData(Gender.Male)]
    [InlineData(Gender.Female)]
    [InlineData(Gender.Unknown)]
    public void UnrestrictedSeries_AppliesToEveryGender(Gender gender)
    {
        // Real data represents "no restriction" as a single empty <requiredGender/> element
        // (Table 5-2: assumed value if empty is "gender of the patient", i.e. always matches),
        // not by omitting the element or listing all three genders explicitly.
        var patient = MakePatient(new DateOnly(2020, 1, 1), gender: gender);
        var assessmentDate = new DateOnly(2020, 3, 1);

        var result = CreateRelevantPatientSeries.Execute(patient, HibSeries, assessmentDate);

        Assert.Contains(result.RelevantSeries, s => s.SeriesType == SeriesType.Standard);
    }

    [Theory]
    [InlineData(Gender.Female, true)]
    [InlineData(Gender.Unknown, true)]  // real data explicitly lists Unknown alongside Female for HPV series
    [InlineData(Gender.Male, true)]     // real data also has separate "HPV male N-dose series" (Standard type) - HPV applies to all genders, just via different series
    public void GenderRestrictedSeries_OnlyRelevantForMatchingGenders(Gender gender, bool expectedRelevant)
    {
        var patient = MakePatient(new DateOnly(2010, 1, 1), gender: gender);
        var assessmentDate = new DateOnly(2023, 1, 1);

        var result = CreateRelevantPatientSeries.Execute(patient, HpvSeries, assessmentDate);

        var anyHpvSeriesRelevant = result.RelevantSeries.Any(s => s.Antigen == "HPV");
        Assert.Equal(expectedRelevant, anyHpvSeriesRelevant);
    }

    [Fact]
    public void FemaleOnlyHpvSeries_NotRelevantForMalePatient()
    {
        // The actually meaningful gender-exclusion check: a Female/Unknown-restricted series
        // specifically must not be selected for a Male patient, even though *some* HPV series
        // (the male-specific ones) legitimately are relevant to him.
        var patient = MakePatient(new DateOnly(2010, 1, 1), gender: Gender.Male);
        var assessmentDate = new DateOnly(2023, 1, 1);

        var result = CreateRelevantPatientSeries.Execute(patient, HpvSeries, assessmentDate);

        Assert.DoesNotContain(result.RelevantSeries, s => s.SeriesName == "HPV 2-dose series");
        Assert.DoesNotContain(result.RelevantSeries, s => s.SeriesName == "HPV 3-dose series");
        Assert.Contains(result.RelevantSeries, s => s.SeriesName == "HPV male 2-dose series");
    }
}
