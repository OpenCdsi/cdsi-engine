using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

/// <summary>
/// End-to-end capstone tests for the §7 per-series forecast orchestrator: real dose history
/// through OrganizeImmunizationHistory -> EvaluateSeriesHistory -> GeneratePatientSeriesForecast.
/// </summary>
public class GeneratePatientSeriesForecastTests
{
    private static readonly ScheduleSupportingData Schedule =
        ScheduleSupportingDataLoader.LoadFile(TestPaths.ScheduleFilePath);

    private static readonly AntigenSeries HepB3DoseSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"))
            .Single(s => s.SeriesName == "HepB 3-dose series");

    private static readonly AntigenImmunityData HepBImmunity =
        AntigenSupportingDataLoader.LoadImmunityData(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    private static readonly AntigenContraindicationData HepBContraindications =
        AntigenSupportingDataLoader.LoadContraindicationData(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    private static readonly Func<string?, bool> NoCompletedSeriesExpected =
        _ => throw new InvalidOperationException("Test fixture shouldn't reach a Completed Series condition.");

    private static Patient MakePatient(DateOnly dob) => new() { PatientId = "p1", DateOfBirth = dob };

    [Fact]
    public void RealHepBSeries_TwoDosesGiven_ForecastsDoseThree_WithVerifiedEarliestDate()
    {
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 1) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "08", DateAdministered = new DateOnly(2020, 3, 1) }
        };

        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var hepBRecords = antigenRecords.Where(r => r.Antigen == "HepB").OrderBy(r => r.DateAdministered).ToArray();

        var seriesHistory = EvaluateSeriesHistory.Execute(
            patient, HepB3DoseSeries, hepBRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.Equal(3, seriesHistory.CurrentTargetDoseNumber); // sanity check before forecasting

        var forecast = GeneratePatientSeriesForecast.Execute(
            patient, HepB3DoseSeries, seriesHistory, assessmentDate: new DateOnly(2020, 9, 1),
            HepBImmunity, HepBContraindications, NoCompletedSeriesExpected);

        Assert.Equal(PatientSeriesStatus.NotComplete, forecast.Status);
        Assert.True(forecast.ShouldForecast);
        Assert.NotNull(forecast.Dates);
        Assert.Equal(3, forecast.ForecastDoseNumber);

        // Hand-traced: Dose 3's minAgeDate (DOB + 24 weeks) is 2020-06-17, which is later than
        // both interval thresholds (~2020-04-22/04-26) and the 1900 seasonal default, so it
        // wins the candidate earliest date MAX - this is also FORECASTDT-1's EarliestDate.
        Assert.Equal(new DateOnly(2020, 6, 17), forecast.Dates!.EarliestDate);

        // Real data: every HepB Dose 3 preferableVaccine entry has forecastVaccineType "N" -
        // none are forecast-eligible, so this is correctly empty, not a bug.
        Assert.Empty(forecast.RecommendedVaccineCvxCodes);

        // The exact real-data case that motivated adding this field: even though none of Dose
        // 3's preferable vaccines are flagged forecast-eligible, they're still clinically valid
        // options for this dose and should surface here (CVX 08 = "Hep B, Adol/peds", among others).
        Assert.Contains("08", forecast.AllPreferableVaccineCvxCodes);
        Assert.NotEmpty(forecast.AllPreferableVaccineCvxCodes);
    }

    [Fact]
    public void RealHepBSeries_AllThreeDosesGiven_StatusComplete_DoesNotForecast()
    {
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 1) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "08", DateAdministered = new DateOnly(2020, 3, 1) },
            new VaccineDoseAdministered { DoseId = "d3", Cvx = "08", DateAdministered = new DateOnly(2020, 9, 1) }
        };

        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var hepBRecords = antigenRecords.Where(r => r.Antigen == "HepB").OrderBy(r => r.DateAdministered).ToArray();

        var seriesHistory = EvaluateSeriesHistory.Execute(
            patient, HepB3DoseSeries, hepBRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.True(seriesHistory.SeriesComplete); // sanity check

        var forecast = GeneratePatientSeriesForecast.Execute(
            patient, HepB3DoseSeries, seriesHistory, assessmentDate: new DateOnly(2021, 1, 1),
            HepBImmunity, HepBContraindications, NoCompletedSeriesExpected);

        Assert.Equal(PatientSeriesStatus.Complete, forecast.Status);
        Assert.False(forecast.ShouldForecast);
        Assert.Null(forecast.Dates);
        Assert.Null(forecast.ForecastDoseNumber);
        Assert.Null(forecast.IsValidRecommendation);
    }

    [Fact]
    public void RealHepBSeries_NoDosesGiven_ForecastsDoseOne()
    {
        var dob = new DateOnly(2024, 1, 1);
        var patient = MakePatient(dob);

        var seriesHistory = EvaluateSeriesHistory.Execute(
            patient, HepB3DoseSeries, Array.Empty<AntigenAdministered>(), Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        var forecast = GeneratePatientSeriesForecast.Execute(
            patient, HepB3DoseSeries, seriesHistory, assessmentDate: new DateOnly(2024, 1, 1),
            HepBImmunity, HepBContraindications, NoCompletedSeriesExpected);

        Assert.Equal(PatientSeriesStatus.NotComplete, forecast.Status);
        Assert.True(forecast.ShouldForecast);
        Assert.Equal(1, forecast.ForecastDoseNumber);

        // Dose 1's own absMinAge/minAge are both "0 days" (birth dose) - earliest date should
        // resolve to the patient's own date of birth, since nothing pushes it later.
        Assert.Equal(dob, forecast.Dates!.EarliestDate);
    }

    [Fact]
    public void RealMenBSeries_ForecastVaccineTypeYFlag_ProducesNonEmptyRecommendedVaccines()
    {
        // Real data: MenB-4C Shared Clinical Decision Making Dose 3 - CVX 328, forecastVaccineType
        // "Y", age window [10 years, 26 years). Wrapped in a synthetic single-dose series so this
        // real per-dose data becomes the (only) current target dose without needing a full 3-dose
        // administered history built up first - a deliberately engineered fixture reusing real
        // reference data, not a claim that a 1-dose "series" is realistic.
        var realDose3 = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Meningococcal_B-508.xml"))
            .Single(s => s.SeriesName == "Meningococcal B 3-dose series MenB-4C Shared Clinical Decision Making")
            .SeriesDoses.Single(d => d.DoseNumber == 3);

        var syntheticSeries = new AntigenSeries
        {
            SeriesName = "Synthetic single-dose MenB series (test fixture)",
            Antigen = "Meningococcal_B",
            SeriesType = SeriesType.Standard,
            RequiredGenders = Array.Empty<Gender>(),
            Indications = Array.Empty<Indication>(),
            SeriesDoses = new[] { realDose3 },
            SeriesAdminGuidance = Array.Empty<string>(),
            SeriesGroupInfo = new SeriesGroupInfo { IsDefaultSeries = true, IsProductPath = false, SeriesGroupName = "Test", SeriesGroup = "1", SeriesPriority = "A", SeriesPreference = 1 }
        };

        var dob = new DateOnly(2000, 1, 1); // age 24 at assessment - within [10y, 26y)
        var patient = MakePatient(dob);
        var emptyImmunity = new AntigenImmunityData { ClinicalHistoryGuidelines = Array.Empty<ImmunityClinicalHistoryGuideline>(), BirthDateRules = Array.Empty<ImmunityBirthDateRule>() };
        var emptyContraindications = new AntigenContraindicationData { AntigenLevel = Array.Empty<AntigenContraindication>(), VaccineLevel = Array.Empty<VaccineContraindication>() };

        var seriesHistory = EvaluateSeriesHistory.Execute(
            patient, syntheticSeries, Array.Empty<AntigenAdministered>(), Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        var forecast = GeneratePatientSeriesForecast.Execute(
            patient, syntheticSeries, seriesHistory, assessmentDate: new DateOnly(2024, 1, 1),
            emptyImmunity, emptyContraindications, NoCompletedSeriesExpected);

        Assert.True(forecast.ShouldForecast);
        Assert.Contains("328", forecast.RecommendedVaccineCvxCodes);
    }
}
