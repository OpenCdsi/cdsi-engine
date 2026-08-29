/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Pipeline;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class ResolveCompletedSeriesGroupsTests
{
    // Real data: two of HepB's own series, one per real series group.
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepB"));

    private static AntigenSeries StandardSeries => HepBSeries.Single(s => s.SeriesName == "HepB 3-dose series"); // group "1"
    private static AntigenSeries RiskSeries => HepBSeries.Single(s => s.SeriesName == "HepB risk Dialysis 4-dose series"); // group "2"

    private static SeriesHistoryResult MakeHistory(bool isComplete) => new()
    {
        DoseResults = Array.Empty<DoseEvaluationRecord>(),
        AllEvaluatedDoses = Array.Empty<Cdsi.Core.Evaluation.EvaluatedAntigenDose>(),
        CurrentTargetDoseNumber = isComplete ? null : 1
    };

    [Fact]
    public void CompleteSeriesInGroup_ResolverReturnsTrueForThatAntigenAndGroup()
    {
        var results = new Dictionary<AntigenSeries, SeriesHistoryResult> { [StandardSeries] = MakeHistory(isComplete: true) };

        var resolver = ResolveCompletedSeriesGroups.Build(results);

        Assert.True(resolver("HepB", "1"));
    }

    [Fact]
    public void IncompleteSeries_ResolverReturnsFalse()
    {
        var results = new Dictionary<AntigenSeries, SeriesHistoryResult> { [StandardSeries] = MakeHistory(isComplete: false) };

        var resolver = ResolveCompletedSeriesGroups.Build(results);

        Assert.False(resolver("HepB", "1"));
    }

    [Fact]
    public void DifferentAntigen_SameGroupString_DoesNotMatch()
    {
        // "1" means something entirely different per antigen file - a complete group "1" series
        // for HepB shouldn't make ANY OTHER antigen's group "1" appear complete.
        var results = new Dictionary<AntigenSeries, SeriesHistoryResult> { [StandardSeries] = MakeHistory(isComplete: true) };

        var resolver = ResolveCompletedSeriesGroups.Build(results);

        Assert.False(resolver("Measles", "1"));
    }

    [Fact]
    public void DifferentGroup_SameAntigen_DoesNotMatch()
    {
        // HepB group "1" (Standard) complete shouldn't make group "2" (Risk) appear complete.
        var results = new Dictionary<AntigenSeries, SeriesHistoryResult> { [StandardSeries] = MakeHistory(isComplete: true) };

        var resolver = ResolveCompletedSeriesGroups.Build(results);

        Assert.False(resolver("HepB", "2"));
    }

    [Fact]
    public void NullSeriesGroupsValue_ReturnsFalse_RegardlessOfAnyCompleteSeries()
    {
        var results = new Dictionary<AntigenSeries, SeriesHistoryResult> { [StandardSeries] = MakeHistory(isComplete: true) };

        var resolver = ResolveCompletedSeriesGroups.Build(results);

        Assert.False(resolver("HepB", null));
    }

    [Fact]
    public void MultipleAntigensAndGroups_EachResolvedIndependently()
    {
        var results = new Dictionary<AntigenSeries, SeriesHistoryResult>
        {
            [StandardSeries] = MakeHistory(isComplete: true),
            [RiskSeries] = MakeHistory(isComplete: false)
        };

        var resolver = ResolveCompletedSeriesGroups.Build(results);

        Assert.True(resolver("HepB", "1"));
        Assert.False(resolver("HepB", "2"));
    }
}
