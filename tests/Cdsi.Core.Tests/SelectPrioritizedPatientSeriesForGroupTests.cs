/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class SelectPrioritizedPatientSeriesForGroupTests
{
    // Real data: HepB series group "1" ("Standard"), 10 series, seriesPreference 1-10.
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    private static AntigenSeries SeriesNamed(string name) => HepBSeries.Single(s => s.SeriesName == name);

    private static readonly DateOnly Dob = new(2000, 1, 1);
    private static readonly DateOnly AssessmentDate = new(2024, 1, 1);

    private static AntigenAdministered MakeDose(DateOnly dateAdministered, string cvx = "08") => new()
    {
        Antigen = "HepB",
        DateAdministered = dateAdministered,
        Cvx = cvx,
        SourceDose = new VaccineDoseAdministered { DoseId = "d", Cvx = cvx, DateAdministered = dateAdministered }
    };

    private static SeriesHistoryResult MakeHistory(int validDoseCount, int? currentTargetDoseNumber, int notSatisfiedCount = 0)
    {
        var doseResults = new List<DoseEvaluationRecord>();
        for (var i = 0; i < validDoseCount; i++)
        {
            // Dates relative to Dob (roughly age 1) rather than a fixed absolute date - this
            // must stay safely within every real series' own maxAgeToStart window (19 years for
            // both HepB series used in these tests) for PreFilterPatientSeries' bullet-2 check
            // to resolve the way each test actually intends, regardless of which Dob is in use.
            doseResults.Add(new DoseEvaluationRecord(MakeDose(Dob.AddYears(1).AddMonths(i)), i + 1, TargetDoseEvaluationResult.Satisfied()));
        }
        for (var i = 0; i < notSatisfiedCount; i++)
        {
            doseResults.Add(new DoseEvaluationRecord(MakeDose(Dob.AddYears(3).AddMonths(i)), null, TargetDoseEvaluationResult.NotSatisfied(EvaluationStatus.NotValid, "Too soon")));
        }

        var evaluated = doseResults
            .Where(r => r.Result.TargetDoseStatus == TargetDoseStatus.Satisfied)
            .Select((r, i) => new EvaluatedAntigenDose("HepB", "08", r.AdministeredDose.DateAdministered, EvaluationStatus.Valid, i + 1))
            .ToArray();

        return new SeriesHistoryResult { DoseResults = doseResults, AllEvaluatedDoses = evaluated, CurrentTargetDoseNumber = currentTargetDoseNumber };
    }

    private static PatientSeriesForecastResult MakeForecast(PatientSeriesStatus status, bool shouldForecast = false) =>
        new() { Status = status, StatusReason = "test", ShouldForecast = shouldForecast };

    [Fact]
    public void SingleScorableSeries_WinsTrivially()
    {
        var series = SeriesNamed("HepB 3-dose series");
        var member = new SeriesGroupMember(series, MakeHistory(1, currentTargetDoseNumber: 2), MakeForecast(PatientSeriesStatus.NotComplete));

        var result = SelectPrioritizedPatientSeriesForGroup.Execute(new[] { member }, Dob, AssessmentDate);

        Assert.Equal(series, result);
    }

    [Fact]
    public void NoScorableSeries_FallsBackToDefaultSeries()
    {
        // Real data: "HepB 3-dose series" IS the real default series for group 1. All members
        // Contraindicated with no non-contraindicated alternative in the group makes none of
        // them candidate-scorable (SELECTB-24), so nothing is scorable at all.
        var defaultSeries = SeriesNamed("HepB 3-dose series");
        var other = SeriesNamed("HepB 4-dose series");

        var members = new[]
        {
            new SeriesGroupMember(defaultSeries, MakeHistory(0, 1), MakeForecast(PatientSeriesStatus.Contraindicated)),
            new SeriesGroupMember(other, MakeHistory(0, 1), MakeForecast(PatientSeriesStatus.Contraindicated))
        };

        var result = SelectPrioritizedPatientSeriesForGroup.Execute(members, Dob, AssessmentDate);

        Assert.Equal(defaultSeries, result);
    }

    [Fact]
    public void TwoOrMoreCompleteSeries_TriggersFullScoring_MostValidDosesWins()
    {
        // Neither §8.2's "exactly one complete" shortcut nor "exactly one scorable" shortcut
        // applies here (2 complete series, tied on nothing else in the shortcut path) - this
        // must fall through to §8.3 classification -> §8.4 scoring -> §8.7 selection.
        var moreValidDoses = SeriesNamed("HepB 3-dose series");
        var fewerValidDoses = SeriesNamed("HepB 4-dose series");

        var members = new[]
        {
            new SeriesGroupMember(moreValidDoses, MakeHistory(4, null), MakeForecast(PatientSeriesStatus.Complete)),
            new SeriesGroupMember(fewerValidDoses, MakeHistory(3, null), MakeForecast(PatientSeriesStatus.Complete))
        };

        var result = SelectPrioritizedPatientSeriesForGroup.Execute(members, Dob, AssessmentDate);

        Assert.Equal(moreValidDoses, result);
    }

    [Fact]
    public void ExactlyOneCompleteAmongMultipleScorable_ShortcutPicksItWithoutScoring()
    {
        var complete = SeriesNamed("HepB 3-dose series");
        var inProcess = SeriesNamed("HepB 4-dose series");

        var members = new[]
        {
            new SeriesGroupMember(complete, MakeHistory(3, null), MakeForecast(PatientSeriesStatus.Complete)),
            new SeriesGroupMember(inProcess, MakeHistory(1, 2), MakeForecast(PatientSeriesStatus.NotComplete))
        };

        var result = SelectPrioritizedPatientSeriesForGroup.Execute(members, Dob, AssessmentDate);

        Assert.Equal(complete, result);
    }

    [Fact]
    public void EmptyMemberList_ReturnsNull()
    {
        var result = SelectPrioritizedPatientSeriesForGroup.Execute(Array.Empty<SeriesGroupMember>(), Dob, AssessmentDate);
        Assert.Null(result);
    }
}
