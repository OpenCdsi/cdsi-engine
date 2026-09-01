/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.Pipeline;
using OpenCdsi.VaxEngine.Core.ReferenceData;
using Xunit;

namespace OpenCdsi.VaxEngine.Core.Tests;

public class DetermineBestPatientSeriesForAntigenTests
{
    // Real data: HepB group "1" (Standard, all 10 series equivalent="2") and group "2" (Risk,
    // most series equivalent="1"). "HepB 3-dose series" (group 1) <-> "HepB risk 3-dose series"
    // (group 2) is a clean, real bidirectional equivalence pair.
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepB"));

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

    // DOB-relative dose dates (not a fixed absolute date) - a real trap caught in the previous
    // round: a fixed date can silently fall outside a series' own maxAgeToStart window depending
    // on which DOB a given test uses, routing PreFilterPatientSeries through an unintended path.
    private static SeriesHistoryResult MakeHistory(int validDoseCount, int? currentTargetDoseNumber)
    {
        var doseResults = new List<DoseEvaluationRecord>();
        for (var i = 0; i < validDoseCount; i++)
        {
            doseResults.Add(new DoseEvaluationRecord(MakeDose(Dob.AddYears(1).AddMonths(i)), i + 1, TargetDoseEvaluationResult.Satisfied()));
        }
        var evaluated = doseResults
            .Select((r, i) => new EvaluatedAntigenDose("HepB", "08", r.AdministeredDose.DateAdministered, EvaluationStatus.Valid, i + 1))
            .ToArray();
        return new SeriesHistoryResult { DoseResults = doseResults, AllEvaluatedDoses = evaluated, CurrentTargetDoseNumber = currentTargetDoseNumber };
    }

    private static PatientSeriesForecastResult MakeForecast(PatientSeriesStatus status, bool shouldForecast = false) =>
        new() { Status = status, StatusReason = "test", ShouldForecast = shouldForecast };

    [Fact]
    public void CompleteSeries_IsAlwaysBest_RegardlessOfEquivalentGroup()
    {
        var group1Series = SeriesNamed("HepB 3-dose series");
        var group2Series = SeriesNamed("HepB risk 3-dose series");

        var members = new[]
        {
            new SeriesGroupMember(group1Series, MakeHistory(3, null), MakeForecast(PatientSeriesStatus.Complete)),
            new SeriesGroupMember(group2Series, MakeHistory(1, 2), MakeForecast(PatientSeriesStatus.NotComplete))
        };

        var best = DetermineBestPatientSeriesForAntigen.Execute(members, Dob, AssessmentDate);

        Assert.Contains(group1Series, best);
    }

    [Fact]
    public void IncompleteRiskSeries_NoEquivalentGroupCompletion_IsBest()
    {
        var group1Series = SeriesNamed("HepB 3-dose series");
        var group2Series = SeriesNamed("HepB risk 3-dose series");

        var members = new[]
        {
            new SeriesGroupMember(group1Series, MakeHistory(1, 2), MakeForecast(PatientSeriesStatus.NotComplete)),
            new SeriesGroupMember(group2Series, MakeHistory(1, 2), MakeForecast(PatientSeriesStatus.NotComplete))
        };

        var best = DetermineBestPatientSeriesForAntigen.Execute(members, Dob, AssessmentDate);

        Assert.Contains(group2Series, best); // Column 2: Risk, incomplete, no equivalent completion
    }

    [Fact]
    public void IncompleteStandardSeries_EquivalentGroupHasRiskPrioritized_IsNotBest()
    {
        var group1Series = SeriesNamed("HepB 3-dose series");
        var group2Series = SeriesNamed("HepB risk 3-dose series");

        var members = new[]
        {
            new SeriesGroupMember(group1Series, MakeHistory(1, 2), MakeForecast(PatientSeriesStatus.NotComplete)),
            new SeriesGroupMember(group2Series, MakeHistory(1, 2), MakeForecast(PatientSeriesStatus.NotComplete))
        };

        var best = DetermineBestPatientSeriesForAntigen.Execute(members, Dob, AssessmentDate);

        // Group 1's Standard series isn't best here - group 2's Risk series already provides
        // supplementary protection (Table 8-14's default "No" case, not Column 3, since Column 3
        // specifically requires NO equivalent group Risk-prioritized series).
        Assert.DoesNotContain(group1Series, best);
    }

    [Fact]
    public void SingleGroupAntigen_IncompleteStandardSeries_NoEquivalentGroupAtAll_IsBest()
    {
        // Real data: Dialysis/Recombivax have no equivalentSeriesGroups at all - but those are
        // Risk-type. Use a group-1-only scenario instead (omit group 2 entirely from the input)
        // to exercise Column 3's "no equivalent group data available at all" path for a
        // Standard series.
        var group1Series = SeriesNamed("HepB 3-dose series");

        var members = new[]
        {
            new SeriesGroupMember(group1Series, MakeHistory(1, 2), MakeForecast(PatientSeriesStatus.NotComplete))
        };

        var best = DetermineBestPatientSeriesForAntigen.Execute(members, Dob, AssessmentDate);

        Assert.Contains(group1Series, best);
    }

    [Fact]
    public void EmptyInput_ReturnsEmptyBestList()
    {
        var best = DetermineBestPatientSeriesForAntigen.Execute(Array.Empty<SeriesGroupMember>(), Dob, AssessmentDate);
        Assert.Empty(best);
    }
}
