using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class IdentifyOnePrioritizedPatientSeriesTests
{
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    private static AntigenSeries SeriesNamed(string name) => HepBSeries.Single(s => s.SeriesName == name);

    [Fact]
    public void Column1_NoScorableSeries_DefaultSeriesWins()
    {
        var defaultSeries = SeriesNamed("HepB 3-dose series"); // real default for group 1

        var result = IdentifyOnePrioritizedPatientSeries.Execute(
            scorableSeries: Array.Empty<AntigenSeries>(), defaultSeriesInGroup: defaultSeries,
            completePatientSeriesInGroup: Array.Empty<AntigenSeries>(), inProcessPatientSeriesInGroup: Array.Empty<AntigenSeries>());

        Assert.Equal(defaultSeries, result);
    }

    [Fact]
    public void Column1_NoScorableSeries_NoDefaultEither_NoWinner()
    {
        var result = IdentifyOnePrioritizedPatientSeries.Execute(
            scorableSeries: Array.Empty<AntigenSeries>(), defaultSeriesInGroup: null,
            completePatientSeriesInGroup: Array.Empty<AntigenSeries>(), inProcessPatientSeriesInGroup: Array.Empty<AntigenSeries>());

        Assert.Null(result);
    }

    [Fact]
    public void Column2_ExactlyOneScorableSeries_WinsOutright()
    {
        var onlyScorable = SeriesNamed("HepB 4-dose series");

        var result = IdentifyOnePrioritizedPatientSeries.Execute(
            scorableSeries: new[] { onlyScorable }, defaultSeriesInGroup: SeriesNamed("HepB 3-dose series"),
            completePatientSeriesInGroup: Array.Empty<AntigenSeries>(), inProcessPatientSeriesInGroup: Array.Empty<AntigenSeries>());

        Assert.Equal(onlyScorable, result);
    }

    [Fact]
    public void Column3_MultipleScorable_ExactlyOneComplete_CompleteWins()
    {
        var complete = SeriesNamed("HepB 4-dose series");
        var other = SeriesNamed("HepB adolescent 2-dose series");

        var result = IdentifyOnePrioritizedPatientSeries.Execute(
            scorableSeries: new[] { complete, other }, defaultSeriesInGroup: null,
            completePatientSeriesInGroup: new[] { complete }, inProcessPatientSeriesInGroup: Array.Empty<AntigenSeries>());

        Assert.Equal(complete, result);
    }

    [Fact]
    public void Column4_NoComplete_ExactlyOneInProcess_InProcessWins()
    {
        var inProcess = SeriesNamed("HepB 4-dose series");
        var other = SeriesNamed("HepB adolescent 2-dose series");

        var result = IdentifyOnePrioritizedPatientSeries.Execute(
            scorableSeries: new[] { inProcess, other }, defaultSeriesInGroup: null,
            completePatientSeriesInGroup: Array.Empty<AntigenSeries>(), inProcessPatientSeriesInGroup: new[] { inProcess });

        Assert.Equal(inProcess, result);
    }

    [Fact]
    public void Column5_NoCompleteNoInProcess_DefaultWins()
    {
        var defaultSeries = SeriesNamed("HepB 3-dose series");
        var other = SeriesNamed("HepB adolescent 2-dose series");

        var result = IdentifyOnePrioritizedPatientSeries.Execute(
            scorableSeries: new[] { defaultSeries, other }, defaultSeriesInGroup: defaultSeries,
            completePatientSeriesInGroup: Array.Empty<AntigenSeries>(), inProcessPatientSeriesInGroup: Array.Empty<AntigenSeries>());

        Assert.Equal(defaultSeries, result);
    }

    [Fact]
    public void Default_MultipleScorable_MultipleCompleteOrNoDefault_NoSingleWinner_FullScoringNeeded()
    {
        var seriesA = SeriesNamed("HepB 3-dose series");
        var seriesB = SeriesNamed("HepB 4-dose series");

        // Two complete series - no single winner via the shortcut, real scoring (§8.3-8.7) needed.
        var result = IdentifyOnePrioritizedPatientSeries.Execute(
            scorableSeries: new[] { seriesA, seriesB }, defaultSeriesInGroup: seriesA,
            completePatientSeriesInGroup: new[] { seriesA, seriesB }, inProcessPatientSeriesInGroup: Array.Empty<AntigenSeries>());

        Assert.Null(result);
    }
}
