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
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepB"));

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
        var tetanusSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("Tetanus"))
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

    [Fact]
    public void RealPertussisStandardSeries_ZeroDoses_SevenYearOld_FastForwardsToDose7NotDose1()
    {
        // The core scenario behind the whole DTaP/Tdap/Td catch-up investigation, reconstructed
        // directly against real Pertussis standard series data: a patient with ZERO administered
        // doses, reaching age 7 exactly. Without assessmentDate supplied (the opt-in), the main
        // loop above never runs at all (there's no administered record to iterate) and
        // CurrentTargetDoseNumber stays at its structural default, Dose 1 - confirmed, spec-
        // faithful per §4.4's own literal text, but clinically wrong (Dose 1's own minAge of
        // 6 weeks anchors a "recommended" date years in the patient's past).
        //
        // With assessmentDate supplied, the new second pass should fast-forward through Doses
        // 1-6 (each of whose real, standalone Evaluation-context Age conditions - confirmed by
        // reading the actual XML `<set>`-by-`<set>`, not assumed - are satisfied by a 7-year-old:
        // Doses 1/2/3/5 skip at Age >= 7 years, Dose 4 at Age >= 4 years, Dose 6 unconditionally
        // at Age >= 7 years), landing on Dose 7 - whose own skip conditions require actual prior
        // valid doses (none apply to a zero-dose patient) and whose own age window (minAge:
        // 7 years, confirmed identical on this series and the "start at 12 months" alternate) is
        // exactly the real, intended, age-anchored recommendation.
        var pertussisSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("Pertussis"))
            .Single(s => s.SeriesName == "Pertussis standard series");

        var dob = new DateOnly(2019, 1, 1);
        var patient = MakePatient(dob);
        var assessmentDate = new DateOnly(2026, 1, 1); // exactly 7 years old

        var withoutFix = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, Array.Empty<AntigenAdministered>(), Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);
        Assert.Equal(1, withoutFix.CurrentTargetDoseNumber); // confirms the structural gap this fix addresses is real

        var withFix = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, Array.Empty<AntigenAdministered>(), Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected, assessmentDate);
        Assert.Equal(7, withFix.CurrentTargetDoseNumber);
        Assert.Empty(withFix.DoseResults); // no administered records exist to have produced any
    }

    [Fact]
    public void RealPertussisStandardSeries_OneValidDoseAsAdult_MainLoopsOwnAgeSkipAlreadyReachesDose8()
    {
        // NOT the "third gap" scenario anymore - that auto-satisfy assumption was implemented,
        // then REVERTED after this exact test's real dotnet test execution disproved the trace
        // it was built on (see EvaluateSeriesHistory's own class doc comment for the full story).
        // Kept and renamed because what it actually demonstrates is still real and worth guarding
        // against regression: an adult patient with exactly one valid prior dose, reconstructed
        // from real corpus case 2020-0004 (DOB 1995-08-05, one Tdap dose CVX115 given 2026-08-05,
        // assessment date the same day - the corpus's own expectedStatus for this dose is
        // 'Valid', already matched by this engine).
        //
        // The original hand-trace assumed Dose 1 satisfies this dose directly, leaving
        // CurrentTargetDoseNumber at Dose 2 after the main loop, needing the fast-forward pass
        // (and then the now-reverted auto-satisfy) to reach Dose 8. Real execution corrected
        // this: Dose 1's own Evaluation-context skip condition (Age >= 7 years, standalone - the
        // same one grounding the "second gap" fix) already fires WITHIN the pre-existing main
        // loop, using the administered dose's own date as reference. For this adult patient, the
        // main loop's OWN, unmodified mechanics try this one CVX115 record against Dose 1 (skip),
        // Dose 2 (skip), ... Dose 6 (skip), landing it on Dose 7, where it genuinely gets
        // Satisfied - advancing straight to Dose 8 with no fast-forward pass needed at all.
        //
        // 2020-0004/2020-0005 themselves remain genuinely unresolved in the full conformance
        // corpus - this test only confirms that Pertussis in isolation reaches the right target
        // dose; the real explanation is now believed to live in Diphtheria/Tetanus behaving
        // differently, or in the multi-antigen merge, not in anything this class does.
        var pertussisSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("Pertussis"))
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

        var withoutSecondPass = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, pertussisRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);
        Assert.Equal(8, withoutSecondPass.CurrentTargetDoseNumber); // the main loop's own age-skip alone already reaches Dose 8

        var withSecondPass = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, pertussisRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected, assessmentDate);
        Assert.Equal(8, withSecondPass.CurrentTargetDoseNumber); // unchanged - the fast-forward pass has nothing left to do here
    }

    [Fact]
    public void DiagnosticOnly_RealPertussis_2013_0016_ThreeDoses_WhereDoesTheMainLoopLand()
    {
        // DIAGNOSTIC, not a fix. Real corpus case 2013-0016 (DOB 2019-07-05, DTaP CVX107 at
        // ~8 months, Td CVX09 at 7 years, Tdap CVX115 one month later) is the counterexample that
        // sank Option 1 (see GeneratePatientSeriesForecast's own class doc comment): the corpus
        // expects Dose 9's own forecast (2027-02-05), not Dose 8's, because this patient has
        // MULTIPLE real doses beyond just whichever satisfied the immediately-prior target dose.
        //
        // Before designing Option 2, checking a basic question with real data rather than
        // continuing to hand-trace: does the MAIN evaluation loop (no assessmentDate, no
        // re-forecast loop involved at all) already land on the right target dose for this
        // patient via its own ordinary within-loop skip mechanics, or does §7.6's re-forecast
        // loop need to intervene here the same way it did for 2020-0004? This determines whether
        // Option 2 even needs to fire for this specific case.
        var pertussisSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("Pertussis"))
            .Single(s => s.SeriesName == "Pertussis standard series");

        var dob = new DateOnly(2019, 7, 5);
        var patient = MakePatient(dob);

        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "107", DateAdministered = new DateOnly(2020, 3, 5) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "09", DateAdministered = new DateOnly(2026, 7, 5) },
            new VaccineDoseAdministered { DoseId = "d3", Cvx = "115", DateAdministered = new DateOnly(2026, 8, 5) }
        };
        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var pertussisRecords = antigenRecords.Where(r => r.Antigen == "Pertussis").OrderBy(r => r.DateAdministered).ToArray();

        var result = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, pertussisRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        // Deliberately asserting a specific guess (9, matching the corpus's own expected target)
        // so a mismatch reveals the real answer directly in the failure message, the same
        // discipline used throughout this investigation rather than assuming the guess is right.
        Assert.Equal(9, result.CurrentTargetDoseNumber);
    }

    [Fact]
    public void DiagnosticOnly_RealPertussis_2013_0016_ThreeDoses_ExactlyWhichDoseSatisfiedWhichTargetDose()
    {
        // DIAGNOSTIC, not a fix. Companion to the diagnostic immediately above, which confirmed
        // the main loop lands at Dose 8 (same starting point as 2020-0004) - but WHICH of the
        // three real doses satisfied Dose 7 (the immediately-prior target dose, the one Option 1
        // would have excluded) matters directly for understanding why Option 1 broke this case:
        // hand-reasoning about this produced a genuine contradiction (CVX09 is in Dose 8's own
        // specific skip-condition CVX list and, if it did NOT satisfy Dose 7, should still have
        // been counted under Option 1's exclusion rule - yet real execution showed Option 1
        // breaking this exact case). Rather than keep guessing, dumping AllEvaluatedDoses's real
        // contents directly - CVX, date, status, and which target dose each satisfied - to get
        // the precise, real answer.
        var pertussisSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("Pertussis"))
            .Single(s => s.SeriesName == "Pertussis standard series");

        var dob = new DateOnly(2019, 7, 5);
        var patient = MakePatient(dob);

        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "107", DateAdministered = new DateOnly(2020, 3, 5) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "09", DateAdministered = new DateOnly(2026, 7, 5) },
            new VaccineDoseAdministered { DoseId = "d3", Cvx = "115", DateAdministered = new DateOnly(2026, 8, 5) }
        };
        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var pertussisRecords = antigenRecords.Where(r => r.Antigen == "Pertussis").OrderBy(r => r.DateAdministered).ToArray();

        var result = EvaluateSeriesHistory.Execute(
            patient, pertussisSeries, pertussisRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        var dump = string.Join(" | ", result.AllEvaluatedDoses.Select(d =>
            $"Cvx={d.Cvx} Date={d.DateAdministered:yyyy-MM-dd} Status={d.Status} SatisfiedTargetDoseNumber={d.SatisfiedTargetDoseNumber}"));

        // Assert.True with a custom message, not a string-equality assertion - xUnit truncates
        // long string diffs (confirmed: the first attempt at this test got cut off at "pos 0"
        // with no way to see past it), but a custom failure message on Assert.True is displayed
        // in full, since it isn't doing a string comparison to show a diff for at all.
        Assert.True(false, $"Count={result.AllEvaluatedDoses.Count} | {dump}");
    }

    [Fact]
    public void DiagnosticOnly_RealTetanus_2013_0016_ThreeDoses_ExactlyWhichDoseSatisfiedWhichTargetDose()
    {
        // DIAGNOSTIC, not a fix. The Pertussis-only version of this diagnostic revealed something
        // important: CVX09 (Td) is completely absent from Pertussis's own AllEvaluatedDoses -
        // confirmed via the real XML, CVX09 maps only to Tetanus and Diphtheria, not Pertussis
        // ("Td" explicitly means no pertussis component, unlike "Tdap"). That means the
        // Pertussis-only investigation so far has been incomplete for this specific patient - for
        // Pertussis alone, only CVX107 and CVX115 count (neither is in Dose 8's own specific
        // skip-condition CVX list), so Pertussis alone likely gets stuck at Dose 8 too, same as
        // 2020-0004. But the corpus's expectation is for the OVERALL DTaP/Tdap/Td GROUP forecast,
        // not Pertussis alone - so Diphtheria or Tetanus, which DO include the CVX09 dose, may be
        // where this patient's forecast is actually correctly reaching Dose 9, with the
        // multi-antigen merge picking their later date over Pertussis's stuck-at-Dose-8 one.
        // Checking Tetanus (identical Dose 7/8/9 structure to Pertussis, already confirmed
        // earlier in this investigation) with the same real patient data to see if this holds.
        var tetanusSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("Tetanus"))
            .Single(s => s.SeriesName == "Tetanus standard series");

        var dob = new DateOnly(2019, 7, 5);
        var patient = MakePatient(dob);

        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "107", DateAdministered = new DateOnly(2020, 3, 5) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "09", DateAdministered = new DateOnly(2026, 7, 5) },
            new VaccineDoseAdministered { DoseId = "d3", Cvx = "115", DateAdministered = new DateOnly(2026, 8, 5) }
        };
        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);
        var tetanusRecords = antigenRecords.Where(r => r.Antigen == "Tetanus").OrderBy(r => r.DateAdministered).ToArray();

        var result = EvaluateSeriesHistory.Execute(
            patient, tetanusSeries, tetanusRecords, Array.Empty<EvaluatedAntigenDose>(),
            Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        var dump = string.Join(" | ", result.AllEvaluatedDoses.Select(d =>
            $"Cvx={d.Cvx} Date={d.DateAdministered:yyyy-MM-dd} Status={d.Status} SatisfiedTargetDoseNumber={d.SatisfiedTargetDoseNumber}"));

        Assert.True(false, $"CurrentTargetDoseNumber={result.CurrentTargetDoseNumber} | Count={result.AllEvaluatedDoses.Count} | {dump}");
    }
}
