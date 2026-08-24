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
/// End-to-end capstone tests: real HepB 3-dose series data run through the full
/// OrganizeImmunizationHistory -> EvaluateSeriesHistory pipeline, exercising all 10 Chapter 6
/// components wired together via EvaluateDoseAgainstTargetDose, plus the §4.4 two-pointer
/// algorithm itself.
/// </summary>
public class EvaluateSeriesHistoryTests
{
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    private static readonly ScheduleSupportingData Schedule =
        ScheduleSupportingDataLoader.LoadFile(TestPaths.ScheduleFilePath);

    private static readonly AntigenSeries HepB3DoseSeries =
        HepBSeries.Single(s => s.SeriesName == "HepB 3-dose series");

    private static readonly Func<string?, bool> NoCompletedSeriesExpected =
        _ => throw new InvalidOperationException("Test fixture shouldn't reach a Completed Series condition.");

    private static Patient MakePatient(DateOnly dob) => new() { PatientId = "p1", DateOfBirth = dob };

    [Fact]
    public void CompleteThreeDoseHepBSeries_AllDosesSatisfied_SeriesComplete()
    {
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);

        // Real CVX 08 (Hep B, Adol/peds) is allowable AND preferable for all 3 doses.
        // Dates chosen to comfortably clear every real age/interval threshold we traced by hand
        // in the design conversation.
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 1) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "08", DateAdministered = new DateOnly(2020, 3, 1) },
            new VaccineDoseAdministered { DoseId = "d3", Cvx = "08", DateAdministered = new DateOnly(2020, 9, 1) }
        };

        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var hepBRecords = antigenRecords.Where(r => r.Antigen == "HepB").OrderBy(r => r.DateAdministered).ToArray();
        Assert.Equal(3, hepBRecords.Length); // sanity check on OrganizeImmunizationHistory's own output

        var result = EvaluateSeriesHistory.Execute(
            patient, HepB3DoseSeries, hepBRecords,
            priorEvaluatedDosesFromOtherAntigens: Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.True(result.SeriesComplete);
        Assert.Null(result.CurrentTargetDoseNumber);
        Assert.Equal(3, result.DoseResults.Count);
        Assert.All(result.DoseResults, r => Assert.Equal(TargetDoseStatus.Satisfied, r.Result.TargetDoseStatus));
        Assert.Equal(new int?[] { 1, 2, 3 }, result.DoseResults.Select(r => r.TargetDoseNumber));
    }

    [Fact]
    public void DoseGivenTooYoung_FailsTargetDose1_TargetDoseDoesNotAdvance()
    {
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);

        // Dose 2's real absolute minimum age is DOB + 24 days ("4 weeks - 4 days"); giving it
        // only 5 days after Dose 1 fails Age directly (Table 6-31 short-circuits on Age before
        // even checking Interval), so target dose 2 remains outstanding.
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 1) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 6) } // 5 days later - too soon
        };

        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var hepBRecords = antigenRecords.Where(r => r.Antigen == "HepB").OrderBy(r => r.DateAdministered).ToArray();

        var result = EvaluateSeriesHistory.Execute(
            patient, HepB3DoseSeries, hepBRecords,
            Array.Empty<EvaluatedAntigenDose>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        // Dose 1 satisfies target dose 1; dose 2 fails target dose 2 on Age ("Too young") and
        // target dose 2 remains outstanding.
        Assert.False(result.SeriesComplete);
        Assert.Equal(2, result.CurrentTargetDoseNumber);
        Assert.Equal(TargetDoseStatus.Satisfied, result.DoseResults[0].Result.TargetDoseStatus);
        Assert.Equal(TargetDoseStatus.NotSatisfied, result.DoseResults[1].Result.TargetDoseStatus);
        Assert.Equal("Too young", result.DoseResults[1].Result.Reason);
    }

    [Fact]
    public void ExtraDoseAfterSeriesComplete_MarkedExtraneous()
    {
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);

        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 1) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "08", DateAdministered = new DateOnly(2020, 3, 1) },
            new VaccineDoseAdministered { DoseId = "d3", Cvx = "08", DateAdministered = new DateOnly(2020, 9, 1) },
            new VaccineDoseAdministered { DoseId = "d4", Cvx = "08", DateAdministered = new DateOnly(2021, 1, 1) } // extra, unneeded 4th dose
        };

        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var hepBRecords = antigenRecords.Where(r => r.Antigen == "HepB").OrderBy(r => r.DateAdministered).ToArray();

        var result = EvaluateSeriesHistory.Execute(
            patient, HepB3DoseSeries, hepBRecords,
            Array.Empty<EvaluatedAntigenDose>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.True(result.SeriesComplete);
        Assert.Equal(4, result.DoseResults.Count);
        var fourthDoseResult = result.DoseResults[3];
        Assert.Equal(TargetDoseStatus.NotSatisfied, fourthDoseResult.Result.TargetDoseStatus);
        Assert.Equal(EvaluationStatus.Extraneous, fourthDoseResult.Result.EvaluationStatus);
        Assert.Null(fourthDoseResult.TargetDoseNumber); // never attempted against any target dose
    }

    [Fact]
    public void NoAdministeredDoses_FirstTargetDoseRemainsOutstanding()
    {
        var patient = MakePatient(new DateOnly(2020, 1, 1));

        var result = EvaluateSeriesHistory.Execute(
            patient, HepB3DoseSeries, Array.Empty<AntigenAdministered>(),
            Array.Empty<EvaluatedAntigenDose>(), Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.False(result.SeriesComplete);
        Assert.Equal(1, result.CurrentTargetDoseNumber);
        Assert.Empty(result.DoseResults);
    }

    [Fact]
    public void RealTetanusTdBooster_RecurringDose_SatisfiesRepeatedlyWithoutBecomingExtraneous()
    {
        // Real data: "Tetanus standard series" Dose 11 is flagged recurringDose="Yes" - a
        // genuine Td-booster scenario (fromPrevious interval, minInt "5 years", earliestRecInt
        // "10 years", no age gate). Wrapped as a synthetic 2-dose series (real Dose 10 + Dose 11
        // objects, extracted directly from the loaded file) so this exercises the real
        // reference data without needing all 11 real doses satisfied first.
        var tetanusSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Tetanus-508.xml"))
            .Single(s => s.SeriesName == "Tetanus standard series");
        var dose10 = tetanusSeries.SeriesDoses.Single(d => d.DoseNumber == 10);
        var dose11 = tetanusSeries.SeriesDoses.Single(d => d.DoseNumber == 11);
        Assert.True(dose11.IsRecurringDose); // sanity check on the real fixture itself

        var syntheticSeries = new AntigenSeries
        {
            SeriesName = "Synthetic Tetanus Dose10+11 (test fixture)",
            Antigen = "Tetanus",
            SeriesType = SeriesType.Standard,
            RequiredGenders = Array.Empty<Gender>(),
            Indications = Array.Empty<Indication>(),
            SeriesDoses = new[] { dose10, dose11 },
            SeriesAdminGuidance = Array.Empty<string>(),
            SeriesGroupInfo = new SeriesGroupInfo { IsDefaultSeries = true, IsProductPath = false, SeriesGroupName = "Test", SeriesGroup = "1", SeriesPriority = "A", SeriesPreference = 1 }
        };

        var dob = new DateOnly(2010, 1, 1);
        var patient = MakePatient(dob);

        // Dose 10 satisfied once (age 11+, past its own minAge), then three separate Td
        // boosters spaced 10 years apart each - well past Dose 11's 5-year absolute floor.
        var doses = new[]
        {
            new AntigenAdministered { Antigen = "Tetanus", Cvx = "09", DateAdministered = new DateOnly(2021, 6, 1), SourceDose = new VaccineDoseAdministered { DoseId = "d1", Cvx = "09", DateAdministered = new DateOnly(2021, 6, 1) } },
            new AntigenAdministered { Antigen = "Tetanus", Cvx = "09", DateAdministered = new DateOnly(2031, 6, 1), SourceDose = new VaccineDoseAdministered { DoseId = "d2", Cvx = "09", DateAdministered = new DateOnly(2031, 6, 1) } },
            new AntigenAdministered { Antigen = "Tetanus", Cvx = "09", DateAdministered = new DateOnly(2041, 6, 1), SourceDose = new VaccineDoseAdministered { DoseId = "d3", Cvx = "09", DateAdministered = new DateOnly(2041, 6, 1) } },
            new AntigenAdministered { Antigen = "Tetanus", Cvx = "09", DateAdministered = new DateOnly(2051, 6, 1), SourceDose = new VaccineDoseAdministered { DoseId = "d4", Cvx = "09", DateAdministered = new DateOnly(2051, 6, 1) } }
        };

        var result = EvaluateSeriesHistory.Execute(
            patient, syntheticSeries, doses, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        // All four doses (Dose 10 once, Dose 11 three separate times) must be Satisfied - the
        // pre-fix behavior would have marked the 2nd and 3rd boosters "Extraneous" instead,
        // since targetIdx would have advanced past Dose 11 (out of bounds) after the first one.
        Assert.Equal(4, result.DoseResults.Count);
        Assert.All(result.DoseResults, r => Assert.Equal(TargetDoseStatus.Satisfied, r.Result.TargetDoseStatus));

        Assert.Equal(10, result.DoseResults[0].TargetDoseNumber);
        Assert.Equal(11, result.DoseResults[1].TargetDoseNumber);
        Assert.Equal(11, result.DoseResults[2].TargetDoseNumber);
        Assert.Equal(11, result.DoseResults[3].TargetDoseNumber);

        // A genuinely recurring series is never "complete" - there's always another booster due.
        Assert.False(result.SeriesComplete);
        Assert.Equal(11, result.CurrentTargetDoseNumber);

        // Three separate EvaluatedAntigenDose entries legitimately share SatisfiedTargetDoseNumber
        // 11 - each represents a genuinely different calendar occurrence of the same recurring
        // requirement, not a data error.
        Assert.Equal(3, result.AllEvaluatedDoses.Count(d => d.SatisfiedTargetDoseNumber == 11));
    }

    [Fact]
    public void RealHepBSeries_NoRecurringDoseFlag_UnchangedBehavior_SeriesCompletesNormally()
    {
        // Regression check: a non-recurring series (real HepB, all 3 doses) must still complete
        // normally after this round's change - targetIdx should advance past every dose exactly
        // as before, since IsRecurringDose is false for real HepB doses.
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);
        var doses = new[]
        {
            new AntigenAdministered { Antigen = "HepB", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 1), SourceDose = new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 1) } },
            new AntigenAdministered { Antigen = "HepB", Cvx = "08", DateAdministered = new DateOnly(2020, 3, 1), SourceDose = new VaccineDoseAdministered { DoseId = "d2", Cvx = "08", DateAdministered = new DateOnly(2020, 3, 1) } },
            new AntigenAdministered { Antigen = "HepB", Cvx = "08", DateAdministered = new DateOnly(2020, 9, 1), SourceDose = new VaccineDoseAdministered { DoseId = "d3", Cvx = "08", DateAdministered = new DateOnly(2020, 9, 1) } }
        };

        var result = EvaluateSeriesHistory.Execute(
            patient, HepB3DoseSeries, doses, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        Assert.True(result.SeriesComplete);
        Assert.Null(result.CurrentTargetDoseNumber);
    }
}
