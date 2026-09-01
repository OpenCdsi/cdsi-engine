/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using Xunit;

namespace OpenCdsi.VaxEngine.Core.Tests;

public class ScoreInProcessPatientSeriesTests
{
    private static readonly DateOnly FarFuture = new(2999, 12, 31);
    private static readonly DateOnly Today = new(2024, 1, 1);

    private static InProcessSeriesCandidate MakeCandidate(
        bool isProductPath = false,
        int validCount = 1,
        DateOnly? finishDate = null,
        DateOnly? maxAgeDate = null,
        int notSatisfiedCount = 1)
    {
        var statuses = Enumerable.Repeat(EvaluationStatus.Valid, validCount).ToArray();
        return new InProcessSeriesCandidate(isProductPath, statuses, finishDate ?? Today, maxAgeDate ?? FarFuture, notSatisfiedCount);
    }

    [Fact]
    public void ProductPathWithAllValidDoses_ScoresPlusTwoOnCondition1()
    {
        var candidate = MakeCandidate(isProductPath: true, validCount: 2);
        // isolate condition 1's effect by keeping every other condition neutral/matching across a single-candidate group
        var score = ScoreInProcessPatientSeries.Execute(candidate, new[] { candidate });

        // condition1: +2 (product+all valid), condition2: +3 (completable, far future max age),
        // condition3: +2 (only candidate, unique max), condition4: +2 (only candidate, unique min),
        // condition5: +1 (only candidate, unique earliest) = 10
        Assert.Equal(10, score);
    }

    [Fact]
    public void NotProductPath_ScoresMinusTwoOnCondition1()
    {
        var candidate = MakeCandidate(isProductPath: false, validCount: 2);
        var score = ScoreInProcessPatientSeries.Execute(candidate, new[] { candidate });

        // condition1: -2 instead of +2 relative to the all-max case above (10 - 4 = 6)
        Assert.Equal(6, score);
    }

    [Fact]
    public void NotCompletable_FinishDateAtOrPastMaxAge_ScoresMinusThreeOnCondition2_AndMinusOneOnCondition5()
    {
        var candidate = MakeCandidate(finishDate: new DateOnly(2024, 6, 1), maxAgeDate: new DateOnly(2024, 6, 1));
        var score = ScoreInProcessPatientSeries.Execute(candidate, new[] { candidate });

        // condition1: -2 (not product), condition2: -3 (not completable), condition3: +2 (unique max),
        // condition4: +2 (unique min), condition5: -1 (not completable, so can't finish earliest) = -2
        Assert.Equal(-2, score);
    }

    [Fact]
    public void MostValidDoses_UniqueMax_ScoresPlusTwoOnCondition3()
    {
        var winner = MakeCandidate(validCount: 3);
        var loser = MakeCandidate(validCount: 1);

        var winnerScore = ScoreInProcessPatientSeries.Execute(winner, new[] { winner, loser });
        var loserScore = ScoreInProcessPatientSeries.Execute(loser, new[] { winner, loser });

        // Condition 3 alone differs by 4 points (+2 vs -2) between winner and loser, all else equal.
        Assert.Equal(4, winnerScore - loserScore);
    }

    [Fact]
    public void MostValidDoses_Tied_ScoresZeroOnCondition3ForBoth()
    {
        var a = MakeCandidate(validCount: 2);
        var b = MakeCandidate(validCount: 2);
        var group = new[] { a, b };

        var scoreA = ScoreInProcessPatientSeries.Execute(a, group);
        var scoreB = ScoreInProcessPatientSeries.Execute(b, group);

        Assert.Equal(scoreA, scoreB); // fully symmetric candidates should score identically
    }

    [Fact]
    public void ClosestToCompletion_UniqueMinimum_ScoresPlusTwoOnCondition4()
    {
        var closer = MakeCandidate(notSatisfiedCount: 1);
        var farther = MakeCandidate(notSatisfiedCount: 3);

        var closerScore = ScoreInProcessPatientSeries.Execute(closer, new[] { closer, farther });
        var fartherScore = ScoreInProcessPatientSeries.Execute(farther, new[] { closer, farther });

        Assert.Equal(4, closerScore - fartherScore); // +2 vs -2 on condition 4 alone
    }

    [Fact]
    public void ClosestToCompletion_TiedMinimum_ScoresZeroForBoth_DespiteSelectB5sStrictWording()
    {
        // Locks in the reconciliation documented on ScoreInProcessPatientSeries: even though
        // SELECTB-5's literal text is a strict "<" that can never be true for two tied series,
        // the scoring function still produces the tied/0 outcome Table 8-9 itself specifies.
        var a = MakeCandidate(notSatisfiedCount: 2);
        var b = MakeCandidate(notSatisfiedCount: 2);
        var group = new[] { a, b };

        var scoreA = ScoreInProcessPatientSeries.Execute(a, group);
        var scoreB = ScoreInProcessPatientSeries.Execute(b, group);

        Assert.Equal(scoreA, scoreB);
    }

    [Fact]
    public void CanFinishEarliest_UniqueEarliestAmongCompletable_ScoresPlusOneOnCondition5()
    {
        var earlier = MakeCandidate(finishDate: new DateOnly(2024, 1, 1));
        var later = MakeCandidate(finishDate: new DateOnly(2024, 6, 1));

        var earlierScore = ScoreInProcessPatientSeries.Execute(earlier, new[] { earlier, later });
        var laterScore = ScoreInProcessPatientSeries.Execute(later, new[] { earlier, later });

        Assert.Equal(2, earlierScore - laterScore); // +1 vs -1 on condition 5 alone
    }

    [Fact]
    public void CanFinishEarliest_TiedEarliest_AllowsTies_ScoresZeroForBoth()
    {
        // SELECTB-11 explicitly uses "on or before" (not strict), so ties here behave
        // straightforwardly, unlike condition 4's reconciliation.
        var a = MakeCandidate(finishDate: new DateOnly(2024, 3, 1));
        var b = MakeCandidate(finishDate: new DateOnly(2024, 3, 1));
        var group = new[] { a, b };

        var scoreA = ScoreInProcessPatientSeries.Execute(a, group);
        var scoreB = ScoreInProcessPatientSeries.Execute(b, group);

        Assert.Equal(scoreA, scoreB);
    }
}
