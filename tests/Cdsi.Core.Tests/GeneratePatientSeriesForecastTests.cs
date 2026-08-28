/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

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
            HepBImmunity, HepBContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

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
            HepBImmunity, HepBContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

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
            HepBImmunity, HepBContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

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
            emptyImmunity, emptyContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.True(forecast.ShouldForecast);
        Assert.Contains("328", forecast.RecommendedVaccineCvxCodes);
    }

    [Fact]
    public void RealMmrVaricellaConflict_RecentMmrDose_PushesVaricellaEarliestDateForward()
    {
        // Real conflict data: MMR (CVX "03") conflicts with Varicella (CVX "21"), conflictEndInterval
        // "28 days". Patient is well past Varicella's own 12-month minAge, so without the conflict,
        // EarliestDate would resolve to that old minAgeDate - the conflict, once wired through
        // priorDosesAllAntigens, should dominate the MAX() instead and push it out to just after
        // the conflict clears.
        var varicellaSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Varicella-508.xml"))
            .Single(s => s.SeriesName == "Varicella childhood 2-dose series");

        var dob = new DateOnly(2019, 1, 1); // age 5 at assessment
        var patient = MakePatient(dob);
        var emptyImmunity = new AntigenImmunityData { ClinicalHistoryGuidelines = Array.Empty<ImmunityClinicalHistoryGuideline>(), BirthDateRules = Array.Empty<ImmunityBirthDateRule>() };
        var emptyContraindications = new AntigenContraindicationData { AntigenLevel = Array.Empty<AntigenContraindication>(), VaccineLevel = Array.Empty<VaccineContraindication>() };

        var seriesHistory = EvaluateSeriesHistory.Execute(
            patient, varicellaSeries, Array.Empty<AntigenAdministered>(), Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        var assessmentDate = new DateOnly(2024, 1, 15);

        var withoutConflict = GeneratePatientSeriesForecast.Execute(
            patient, varicellaSeries, seriesHistory, assessmentDate, emptyImmunity, emptyContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        var mmrGivenRecently = new[] { new PriorVaccineDoseAdministered("03", new DateOnly(2024, 1, 1), PriorDoseEvaluationStatus.Valid) };
        var withConflict = GeneratePatientSeriesForecast.Execute(
            patient, varicellaSeries, seriesHistory, assessmentDate, emptyImmunity, emptyContraindications,
            mmrGivenRecently, Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.True(withoutConflict.ShouldForecast);
        Assert.True(withConflict.ShouldForecast);

        // Without the conflict, EarliestDate resolves to Varicella's own (long-past) minAgeDate.
        Assert.Equal(new DateOnly(2020, 1, 1), withoutConflict.Dates!.EarliestDate);

        // With the conflict wired through, the MMR dose's conflict end date (2024-01-01 + 28
        // days) dominates the MAX() instead, since it's later than the old minAgeDate.
        Assert.Equal(new DateOnly(2024, 1, 29), withConflict.Dates!.EarliestDate);
    }

    [Fact]
    public void InadvertentAdministrationInDoseResults_PushesEarliestDateForward()
    {
        // Extraction wiring test: seriesHistory.DoseResults already carries §6.3's own
        // "Inadvertent Administration" reason string on real NotSatisfied/NotValid results
        // (confirmed by re-reading EvaluateDoseAgainstTargetDose's source before writing this) -
        // this proves GeneratePatientSeriesForecast actually extracts and uses it, not just that
        // the filter compiles. Real COVID-19 data has genuine inadvertentVaccine entries (e.g.
        // CVX "230"), but reconstructing a full real dose history isn't needed to test this
        // specific extraction - a synthetic DoseResults entry with the real reason string,
        // against the same real Varicella fixture used above, isolates just this piece.
        var varicellaSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Varicella-508.xml"))
            .Single(s => s.SeriesName == "Varicella childhood 2-dose series");

        var dob = new DateOnly(2019, 1, 1);
        var patient = MakePatient(dob);
        var emptyImmunity = new AntigenImmunityData { ClinicalHistoryGuidelines = Array.Empty<ImmunityClinicalHistoryGuideline>(), BirthDateRules = Array.Empty<ImmunityBirthDateRule>() };
        var emptyContraindications = new AntigenContraindicationData { AntigenLevel = Array.Empty<AntigenContraindication>(), VaccineLevel = Array.Empty<VaccineContraindication>() };

        var inadvertentDoseRecord = new DoseEvaluationRecord(
            new AntigenAdministered
            {
                Antigen = "Varicella",
                Cvx = "94",
                DateAdministered = new DateOnly(2024, 1, 10),
                SourceDose = new VaccineDoseAdministered { DoseId = "d1", Cvx = "94", DateAdministered = new DateOnly(2024, 1, 10) }
            },
            TargetDoseNumber: null,
            Result: TargetDoseEvaluationResult.NotSatisfied(EvaluationStatus.NotValid, "Inadvertent Administration"));

        var seriesHistory = new SeriesHistoryResult
        {
            DoseResults = new[] { inadvertentDoseRecord },
            AllEvaluatedDoses = Array.Empty<EvaluatedAntigenDose>(),
            CurrentTargetDoseNumber = 1
        };

        var assessmentDate = new DateOnly(2024, 1, 15);
        var forecast = GeneratePatientSeriesForecast.Execute(
            patient, varicellaSeries, seriesHistory, assessmentDate, emptyImmunity, emptyContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.True(forecast.ShouldForecast);
        // 2024-01-10 (the inadvertent administration date) dominates the MAX() over Varicella's
        // own long-past minAgeDate (2020-01-01).
        Assert.Equal(new DateOnly(2024, 1, 10), forecast.Dates!.EarliestDate);
    }

    [Fact]
    public void RealPertussisSeries_IntervalPriorityOverrideFlag_ForecastIsMarkedAsPriority()
    {
        // Real data: "Pertussis standard series" Dose 2's own interval has intervalPriority
        // "override" (the real-world equivalent of FORECASTPRIORITY-1's "Y" flag - see
        // MultipleAntigenVaccineGroup's own doc comment for why "override" is the value that
        // actually appears in real data). Pertussis genuinely belongs to the real multi-antigen
        // DTaP/Tdap/Td vaccine group, which is exactly the scenario this field exists to support.
        var pertussisSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Pertussis-508.xml"))
            .Single(s => s.SeriesName == "Pertussis standard series");

        var dob = new DateOnly(2024, 1, 1);
        var patient = MakePatient(dob);
        var emptyImmunity = new AntigenImmunityData { ClinicalHistoryGuidelines = Array.Empty<ImmunityClinicalHistoryGuideline>(), BirthDateRules = Array.Empty<ImmunityBirthDateRule>() };
        var emptyContraindications = new AntigenContraindicationData { AntigenLevel = Array.Empty<AntigenContraindication>(), VaccineLevel = Array.Empty<VaccineContraindication>() };

        // One real Dose 1 (CVX "20", DTaP alone), safely past its own 6-week minAge.
        var doses = new[] { new VaccineDoseAdministered { DoseId = "d1", Cvx = "20", DateAdministered = dob.AddDays(56) } };
        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var pertussisRecords = antigenRecords.Where(r => r.Antigen == "Pertussis").OrderBy(r => r.DateAdministered).ToArray();

        var seriesHistory = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, pertussisRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.Equal(2, seriesHistory.CurrentTargetDoseNumber); // sanity check: Dose 1 satisfied, now forecasting Dose 2

        var forecast = GeneratePatientSeriesForecast.Execute(
            patient, pertussisSeries, seriesHistory, assessmentDate: dob.AddMonths(6),
            emptyImmunity, emptyContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.True(forecast.ShouldForecast);
        Assert.True(forecast.IsPriorityForecast);
    }

    [Fact]
    public void RealHepBSeries_NoIntervalPriorityFlag_ForecastIsNotMarkedAsPriority()
    {
        // Contrast fixture: real HepB Dose 3 intervals have no intervalPriority flag at all
        // (confirmed elsewhere in this project's grounding work) - IsPriorityForecast should be
        // false, not just "true by default."
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

        var forecast = GeneratePatientSeriesForecast.Execute(
            patient, HepB3DoseSeries, seriesHistory, assessmentDate: new DateOnly(2020, 9, 1),
            HepBImmunity, HepBContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.True(forecast.ShouldForecast);
        Assert.False(forecast.IsPriorityForecast);
    }

    [Fact]
    public void RealHibSeries_LateDose2_MakesDose3ForecastInvalid_RetriesAndReturnsDose4()
    {
        // §7.6 Validate Recommendation, the spec's own worked example pattern (a catch-up dose
        // forecast that becomes stale by the time its own earliest date arrives), reconstructed
        // against real "Hib start at 2 months 4-dose series" data - not the spec's own narrative
        // example verbatim (that one has some genuine ambiguity about which exact series/dose
        // pairing it refers to), but a scenario hand-traced precisely against this series' own
        // real numbers before writing any assertion.
        //
        // Dose 1 given early (age 8 weeks). Dose 2 given deliberately LATE (age ~11.5 months) -
        // still satisfies Dose 2's own requirements (minAge 10 weeks, 4-week minInt from Dose 1),
        // but pushes Dose 3's own candidateEarliestDate (MAX of its 14-week minAge and its
        // 4-week minInt from Dose 2) to 2021-01-12 - past Dose 3's real Forecast-context skip
        // condition ("Age >= 12 months" at DOB 2020-01-01, i.e. on or after 2021-01-01). Dose 3's
        // own forecast should therefore be invalid, forcing a retry against Dose 4 - which has no
        // Forecast-context skip condition of its own, and whose "fromPrevious" interval
        // correctly references the real previous ADMINISTERED dose (Dose 2, since Dose 3 was
        // never actually given, only forecasted and rejected).
        var hibSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Hib-508.xml"))
            .Single(s => s.SeriesName == "Hib start at 2 months 4-dose series");

        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);
        var emptyImmunity = new AntigenImmunityData { ClinicalHistoryGuidelines = Array.Empty<ImmunityClinicalHistoryGuideline>(), BirthDateRules = Array.Empty<ImmunityBirthDateRule>() };
        var emptyContraindications = new AntigenContraindicationData { AntigenLevel = Array.Empty<AntigenContraindication>(), VaccineLevel = Array.Empty<VaccineContraindication>() };

        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "48", DateAdministered = new DateOnly(2020, 2, 26) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "48", DateAdministered = new DateOnly(2020, 12, 15) }
        };
        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var hibRecords = antigenRecords.Where(r => r.Antigen == "Hib").OrderBy(r => r.DateAdministered).ToArray();

        var seriesHistory = EvaluateSeriesHistory.Execute(
            patient, hibSeries, hibRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.Equal(3, seriesHistory.CurrentTargetDoseNumber); // sanity check: Dose 1+2 satisfied, Dose 3 is what evaluation says comes next

        var forecast = GeneratePatientSeriesForecast.Execute(
            patient, hibSeries, seriesHistory, assessmentDate: new DateOnly(2020, 12, 20),
            emptyImmunity, emptyContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.True(forecast.ShouldForecast);
        Assert.Equal(true, forecast.IsValidRecommendation); // Dose 3's own attempt would have been invalid - this must reflect Dose 4's, which is valid
        Assert.Equal(new DateOnly(2021, 2, 9), forecast.Dates!.EarliestDate); // Dose 4's own interval math (Dose 2 + 8 weeks), not Dose 3's (which would have been 2021-01-12)
    }

    [Fact]
    public void DiagnosticOnly_RealPertussisFullForecast_OneValidDoseAsAdult_WhereDoesTheReForecastLoopActuallyLand()
    {
        // DIAGNOSTIC, not a fix - reverted back to this state after the Option 1 fix that once
        // lived here was itself reverted (see this class's own doc comment for the full story:
        // Option 1 fixed 2020-0004 but caused a net regression elsewhere - 2013-0016's multi-dose
        // scenario is the clean counterexample). Confirms real corpus case 2020-0004's own
        // re-forecast loop still cascades to a wrong result on the current, reverted baseline.
        // The assertion intentionally checks against the corpus's own expected date (2026-09-02)
        // - if this fails, the failure message's "Actual:" value is the real, current answer.
        var pertussisSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Pertussis-508.xml"))
            .Single(s => s.SeriesName == "Pertussis standard series");
        var emptyImmunity = new AntigenImmunityData { ClinicalHistoryGuidelines = Array.Empty<ImmunityClinicalHistoryGuideline>(), BirthDateRules = Array.Empty<ImmunityBirthDateRule>() };
        var emptyContraindications = new AntigenContraindicationData { AntigenLevel = Array.Empty<AntigenContraindication>(), VaccineLevel = Array.Empty<VaccineContraindication>() };

        var dob = new DateOnly(1995, 8, 5);
        var patient = MakePatient(dob);
        var assessmentDate = new DateOnly(2026, 8, 5);

        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "115", DateAdministered = new DateOnly(2026, 8, 5) }
        };
        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var pertussisRecords = antigenRecords.Where(r => r.Antigen == "Pertussis").OrderBy(r => r.DateAdministered).ToArray();

        var seriesHistory = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, pertussisRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected, assessmentDate);
        Assert.Equal(8, seriesHistory.CurrentTargetDoseNumber); // sanity check, confirmed by the real Pertussis test in EvaluateSeriesHistoryTests

        var forecast = GeneratePatientSeriesForecast.Execute(
            patient, pertussisSeries, seriesHistory, assessmentDate,
            emptyImmunity, emptyContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.Equal(new DateOnly(2026, 9, 2), forecast.Dates!.EarliestDate);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void DiagnosticOnly_RealPertussisDose8Through11_EachStartingPointIndependently(int startingDoseNumber)
    {
        // DIAGNOSTIC, not a fix - reverted back to this state after the Option 1 fix that once
        // lived here was itself reverted (see this class's own doc comment for the full story).
        // On the CURRENT, reverted baseline, forces Execute's own internal loop to start at EACH
        // candidate dose independently, via a synthetic SeriesHistoryResult with
        // CurrentTargetDoseNumber set directly (the real AllEvaluatedDoses/DoseResults are reused
        // unchanged regardless of where the loop starts), confirming all four converge on the
        // same wrong result - the finding that originally pointed at mostRecentAdministeredDate
        // and, from there, at the real root cause in ValidateRecommendation's own doseCount check.
        var pertussisSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Pertussis-508.xml"))
            .Single(s => s.SeriesName == "Pertussis standard series");
        var emptyImmunity = new AntigenImmunityData { ClinicalHistoryGuidelines = Array.Empty<ImmunityClinicalHistoryGuideline>(), BirthDateRules = Array.Empty<ImmunityBirthDateRule>() };
        var emptyContraindications = new AntigenContraindicationData { AntigenLevel = Array.Empty<AntigenContraindication>(), VaccineLevel = Array.Empty<VaccineContraindication>() };

        var dob = new DateOnly(1995, 8, 5);
        var patient = MakePatient(dob);
        var assessmentDate = new DateOnly(2026, 8, 5);

        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "115", DateAdministered = new DateOnly(2026, 8, 5) }
        };
        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var pertussisRecords = antigenRecords.Where(r => r.Antigen == "Pertussis").OrderBy(r => r.DateAdministered).ToArray();

        var realSeriesHistory = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, pertussisRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected, assessmentDate);

        var forcedSeriesHistory = new SeriesHistoryResult
        {
            DoseResults = realSeriesHistory.DoseResults,
            AllEvaluatedDoses = realSeriesHistory.AllEvaluatedDoses,
            CurrentTargetDoseNumber = startingDoseNumber
        };

        var forecast = GeneratePatientSeriesForecast.Execute(
            patient, pertussisSeries, forcedSeriesHistory, assessmentDate,
            emptyImmunity, emptyContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        var expected = new DateOnly(2026, 8, 5); // all four starting points converge on the same wrong result on the reverted baseline

        Assert.Equal(expected, forecast.Dates?.EarliestDate);
    }

    [Fact]
    public void DiagnosticOnly_RealPertussisAllEvaluatedDoses_ContentsForThisAdultPatient()
    {
        // DIAGNOSTIC, not a fix. The isolated ForecastIntervalDatesTests diagnostic confirmed
        // ForecastIntervalDates.LatestMinIntervalDate itself is correct (2026-09-02) given a
        // trivial resolver. The real pipeline's own resolver (GeneratePatientSeriesForecast's
        // private BuildIntervalReferenceResolver, which can't be unit-tested directly) filters
        // seriesHistory.AllEvaluatedDoses to `Status is Valid or NotValid`, orders by date
        // descending, and takes the first DateAdministered - manually replicating that exact
        // LINQ expression here, against this same patient's REAL AllEvaluatedDoses, to check
        // whether it produces 2026-08-05 as expected or something else (null, wrong Status,
        // wrong count) that would explain why the real pipeline's forecast doesn't match this
        // function's own already-confirmed-correct behavior.
        var pertussisSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Pertussis-508.xml"))
            .Single(s => s.SeriesName == "Pertussis standard series");

        var dob = new DateOnly(1995, 8, 5);
        var patient = MakePatient(dob);
        var assessmentDate = new DateOnly(2026, 8, 5);

        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "115", DateAdministered = new DateOnly(2026, 8, 5) }
        };
        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var pertussisRecords = antigenRecords.Where(r => r.Antigen == "Pertussis").OrderBy(r => r.DateAdministered).ToArray();

        var realSeriesHistory = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, pertussisRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected, assessmentDate);

        Assert.Equal(1, realSeriesHistory.AllEvaluatedDoses.Count);
        Assert.Equal(EvaluationStatus.Valid, realSeriesHistory.AllEvaluatedDoses[0].Status);
        Assert.Equal(new DateOnly(2026, 8, 5), realSeriesHistory.AllEvaluatedDoses[0].DateAdministered);

        // The exact replicated FromPrevious resolution logic:
        var resolvedFromPrevious = realSeriesHistory.AllEvaluatedDoses
            .Where(d => d.Status is EvaluationStatus.Valid or EvaluationStatus.NotValid)
            .OrderByDescending(d => d.DateAdministered)
            .FirstOrDefault()?.DateAdministered;

        Assert.Equal(new DateOnly(2026, 8, 5), resolvedFromPrevious);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void DiagnosticOnly_RealPertussisDose8Through11_ComputeForecastForTargetDose_CalledDirectlyBypassingTheLoop(int doseNumber)
    {
        // DIAGNOSTIC, not a fix. Every individually-tested piece of the interval computation
        // checked out correct in isolation (see the diagnostics immediately above and in
        // ForecastIntervalDatesTests), yet the real loop still produces a result none of them
        // predicted. ComputeForecastForTargetDose was made internal (from private) specifically
        // so this test could call it directly for ONE dose at a time - completely bypassing
        // Execute's own re-forecast loop and its retry mechanics - to see exactly what this
        // function itself produces when actually run for real, for each real dose, in isolation.
        //
        // Extended from Dose 8 alone (already confirmed correct: 2026-09-02) to cover 9, 10, 11
        // to test a specific hypothesis: mostRecentAdministeredDate is computed as the Max
        // DateAdministered across ALL of seriesHistory.AllEvaluatedDoses, UNCONDITIONALLY -
        // confirmed by reading ComputeForecastForTargetDose's own code - regardless of which
        // target dose is currently being forecast. For this patient (one dose, given ON the
        // assessment date), that floor is 2026-08-05 for every single dose number. Dose 9's own
        // real 6-month interval (2027-02-05) should still beat that floor. But Dose 10 and 11
        // both use a FromMostRecent interval reference filtered to CVX codes that exclude this
        // patient's one dose (resolving to null), leaving only their own minAge (11 years,
        // trivially met, decades in this patient's past) to compete against the 2026-08-05 floor
        // in the final Max - which the floor would win, producing "today" instead of their own
        // genuine, much-earlier age-based date. If Dose 10/11 come back as 2026-08-05 here, that
        // confirms mostRecentAdministeredDate is the real, final piece of this bug - not the
        // loop's retry logic, which has checked out correct at every other point tested so far.
        var pertussisSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Pertussis-508.xml"))
            .Single(s => s.SeriesName == "Pertussis standard series");
        var targetDose = pertussisSeries.SeriesDoses.Single(d => d.DoseNumber == doseNumber);
        var emptyImmunity = new AntigenImmunityData { ClinicalHistoryGuidelines = Array.Empty<ImmunityClinicalHistoryGuideline>(), BirthDateRules = Array.Empty<ImmunityBirthDateRule>() };
        var emptyContraindications = new AntigenContraindicationData { AntigenLevel = Array.Empty<AntigenContraindication>(), VaccineLevel = Array.Empty<VaccineContraindication>() };

        var dob = new DateOnly(1995, 8, 5);
        var patient = MakePatient(dob);
        var assessmentDate = new DateOnly(2026, 8, 5);

        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "115", DateAdministered = new DateOnly(2026, 8, 5) }
        };
        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var pertussisRecords = antigenRecords.Where(r => r.Antigen == "Pertussis").OrderBy(r => r.DateAdministered).ToArray();

        var seriesHistory = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, pertussisRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected, assessmentDate);

        var hasNotSatisfiedTargetDose = !seriesHistory.SeriesComplete;
        var hasSatisfiedTargetDose = seriesHistory.AllEvaluatedDoses.Any(d => d.SatisfiedTargetDoseNumber is not null);

        var attempt = GeneratePatientSeriesForecast.ComputeForecastForTargetDose(
            patient, pertussisSeries, seriesHistory, assessmentDate, emptyImmunity, emptyContraindications,
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected,
            targetDose, hasNotSatisfiedTargetDose, hasSatisfiedTargetDose);

        Assert.True(attempt.ShouldForecast);
        var expected = doseNumber switch
        {
            8 => new DateOnly(2026, 9, 2),  // confirmed correct in the original single-dose diagnostic
            9 => new DateOnly(2027, 2, 5),  // real 6-month interval from the same 2026-08-05 dose
            10 => new DateOnly(2026, 8, 5), // testing the mostRecentAdministeredDate-floor hypothesis
            11 => new DateOnly(2026, 8, 5), // same hypothesis
            _ => throw new ArgumentOutOfRangeException(nameof(doseNumber))
        };
        Assert.Equal(expected, attempt.Dates?.EarliestDate);
    }
}
