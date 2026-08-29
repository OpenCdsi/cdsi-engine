/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class SelectPrioritizedPatientSeriesTests
{
    // Real data: HepB series group "1" ("Standard") has 10 series with distinct seriesPreference
    // values 1 through 10 (1 = best/most preferred), in this exact order.
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    private static AntigenSeries SeriesNamed(string name) => HepBSeries.Single(s => s.SeriesName == name);

    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        var result = SelectPrioritizedPatientSeries.Execute(Array.Empty<ScoredPatientSeries>());
        Assert.Null(result);
    }

    [Fact]
    public void ClearHighestScore_WinsOutright_NoTieBreakNeeded()
    {
        var winner = SeriesNamed("HepB Twinrix 4-dose series"); // preference 10 (worst) but highest score
        var loser = SeriesNamed("HepB 3-dose series"); // preference 1 (best) but lower score

        var result = SelectPrioritizedPatientSeries.Execute(new[]
        {
            new ScoredPatientSeries(winner, 8),
            new ScoredPatientSeries(loser, 3)
        });

        Assert.Equal(winner, result); // score wins over preference when there's no tie
    }

    [Fact]
    public void TiedScore_TieBrokenByBestSeriesPreference()
    {
        var betterPreference = SeriesNamed("HepB 3-dose series");   // preference 1
        var worsePreference = SeriesNamed("HepB Twinrix 4-dose series"); // preference 10

        var result = SelectPrioritizedPatientSeries.Execute(new[]
        {
            new ScoredPatientSeries(betterPreference, 5),
            new ScoredPatientSeries(worsePreference, 5)
        });

        Assert.Equal(betterPreference, result);
    }

    [Fact]
    public void TiedScore_LowerPreferenceNumberAlwaysWinsRegardlessOfOrder()
    {
        var seriesA = SeriesNamed("HepB 4-dose series");        // preference 2
        var seriesB = SeriesNamed("HepB adolescent 2-dose series"); // preference 3

        var result = SelectPrioritizedPatientSeries.Execute(new[]
        {
            new ScoredPatientSeries(seriesB, 7), // listed first, but worse preference
            new ScoredPatientSeries(seriesA, 7)
        });

        Assert.Equal(seriesA, result); // preference 2 beats preference 3
    }

    [Fact]
    public void MultipleSeriesTiedOnScore_OnlyTheLowestScorersAreExcluded()
    {
        var top1 = SeriesNamed("HepB 3-dose series");   // preference 1
        var top2 = SeriesNamed("HepB 4-dose series");   // preference 2
        var lowerScore = SeriesNamed("HepB Twinrix 4-dose series"); // preference 10, but lower score

        var result = SelectPrioritizedPatientSeries.Execute(new[]
        {
            new ScoredPatientSeries(top1, 5),
            new ScoredPatientSeries(top2, 5),
            new ScoredPatientSeries(lowerScore, 2)
        });

        Assert.Equal(top1, result); // among the tied top scorers, preference 1 wins
    }

    [Fact]
    public void TiedScoreAndTiedPreference_DeterministicFallbackByName()
    {
        // Two distinct series, contrived to share the same score AND the same seriesPreference
        // value (impossible within one real series group, but the function doesn't assume
        // uniqueness). REVISED after a real bug was found and fixed here (see this class's own
        // doc comment): §8.8's own precondition requires exactly one winner per group, so a tie
        // surviving both the score comparison and the seriesPreference comparison now falls back
        // to a deterministic choice (ordered by series name) rather than giving up with null -
        // the previous version of this test asserted the old, now-corrected "give up" behavior.
        var a = SeriesNamed("HepB 3-dose series");           // preference 1
        var b = SeriesNamed("HepB risk 3-dose series");       // different group, preference 1 too

        var result = SelectPrioritizedPatientSeries.Execute(new[]
        {
            new ScoredPatientSeries(a, 5),
            new ScoredPatientSeries(b, 5)
        });

        Assert.Equal(a, result); // "HepB 3-dose series" sorts before "HepB risk 3-dose series" (ordinal: '3' < 'r')
    }

    [Fact]
    public void TiedScore_NoTiedCandidateHasAnyPreference_DeterministicFallbackByName()
    {
        // The real bug this class's own doc comment describes, reconstructed directly with the
        // exact real data that caused it: real corpus case 2024-0032 (MenB, 4 tied "Shared
        // Clinical Decision Making" series, none with a seriesPreference at all - confirmed real
        // data, all 6 real Meningococcal B series have seriesPreference=None) used to return
        // null here, cascading silently all the way up to the entire Meningococcal B vaccine
        // group vanishing from the final forecast output.
        var meningococcalBSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Meningococcal_B-508.xml"));
        var seriesWithoutPreference1 = meningococcalBSeries.Single(s => s.SeriesName == "Meningococcal B 2-dose series MenB-4C Shared Clinical Decision Making");
        var seriesWithoutPreference2 = meningococcalBSeries.Single(s => s.SeriesName == "Meningococcal B 2-dose series MenB-FHbp Shared Clinical Decision Making");

        var result = SelectPrioritizedPatientSeries.Execute(new[]
        {
            new ScoredPatientSeries(seriesWithoutPreference1, 5),
            new ScoredPatientSeries(seriesWithoutPreference2, 5)
        });

        Assert.NotNull(result); // the real bug: this used to be null
        Assert.Equal(seriesWithoutPreference1, result); // "...MenB-4C..." sorts before "...MenB-FHbp..." (ordinal: '4' < 'F')
    }
}
