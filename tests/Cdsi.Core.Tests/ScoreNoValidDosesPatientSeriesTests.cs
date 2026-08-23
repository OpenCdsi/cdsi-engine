using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class ScoreNoValidDosesPatientSeriesTests
{
    private static NoValidDosesSeriesCandidate Make(bool isProductPath = false, bool isCompletable = true, DateOnly? startDate = null) =>
        new(isProductPath, isCompletable, startDate ?? new DateOnly(2024, 1, 1));

    [Fact]
    public void ProductPatientSeries_IsPenalized_NotRewarded()
    {
        // Deliberate sign inversion vs §8.5 - confirm it's actually implemented, not "fixed" to match §8.5.
        var product = Make(isProductPath: true);
        var notProduct = Make(isProductPath: false);

        var productScore = ScoreNoValidDosesPatientSeries.Execute(product, new[] { product });
        var notProductScore = ScoreNoValidDosesPatientSeries.Execute(notProduct, new[] { notProduct });

        Assert.True(productScore < notProductScore);
        Assert.Equal(-2, productScore - notProductScore); // -1 vs +1 on condition 3 alone
    }

    [Fact]
    public void Completable_ScoresPlusOneOnCondition2()
    {
        var completable = Make(isCompletable: true);
        var notCompletable = Make(isCompletable: false);

        var diff = ScoreNoValidDosesPatientSeries.Execute(completable, new[] { completable })
                 - ScoreNoValidDosesPatientSeries.Execute(notCompletable, new[] { notCompletable });

        Assert.Equal(2, diff); // +1 vs -1
    }

    [Fact]
    public void UniqueEarliestStartDate_ScoresPlusOneOnCondition1()
    {
        var earlier = Make(startDate: new DateOnly(2024, 1, 1));
        var later = Make(startDate: new DateOnly(2025, 1, 1));

        var diff = ScoreNoValidDosesPatientSeries.Execute(earlier, new[] { earlier, later })
                 - ScoreNoValidDosesPatientSeries.Execute(later, new[] { earlier, later });

        Assert.Equal(2, diff); // +1 vs -1 on condition 1 alone
    }

    [Fact]
    public void TiedEarliestStartDate_ScoresZero_DespiteSelectB14sStrictWording()
    {
        var a = Make(startDate: new DateOnly(2024, 1, 1));
        var b = Make(startDate: new DateOnly(2024, 1, 1));
        var group = new[] { a, b };

        var scoreA = ScoreNoValidDosesPatientSeries.Execute(a, group);
        var scoreB = ScoreNoValidDosesPatientSeries.Execute(b, group);

        Assert.Equal(scoreA, scoreB);
    }

    [Fact]
    public void NoStartDateAtAll_TreatedAsNotEarliest()
    {
        var withDate = Make(startDate: new DateOnly(2024, 1, 1));
        var withoutDate = new NoValidDosesSeriesCandidate(false, true, null);

        var diff = ScoreNoValidDosesPatientSeries.Execute(withDate, new[] { withDate, withoutDate })
                 - ScoreNoValidDosesPatientSeries.Execute(withoutDate, new[] { withDate, withoutDate });

        Assert.Equal(2, diff); // +1 vs -1
    }

    [Fact]
    public void RealHepBGroup2_TieStructure_DialysisStartsEarlierThanSixtyYearGroup()
    {
        // Real data: HepB series group "2" has 8 Risk series - Dialysis and Recombivax both
        // have minAgeToStart "20 years" (tied with each other, earlier); the other 6 all share
        // "60 years" (tied with each other, later). Uses the real SeriesGroupInfo data directly
        // rather than synthetic dates.
        //
        // Comparing Dialysis against "HepB risk 3-dose series" specifically (not Recombivax) -
        // both are productPath "No", so condition 3 is equal between them, cleanly isolating
        // condition 1's (start-date) contribution. Recombivax is productPath "Yes", which would
        // otherwise muddy a direct score comparison with a second condition's difference.
        var group2 = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"))
            .Where(s => s.SeriesGroupInfo.SeriesGroup == "2")
            .ToArray();
        var dob = new DateOnly(2000, 1, 1);

        var candidatesBySeries = group2.ToDictionary(
            s => s.SeriesName,
            s => new NoValidDosesSeriesCandidate(s.SeriesGroupInfo.IsProductPath, true, s.SeriesGroupInfo.MinAgeToStartDate(dob)));

        var allCandidates = candidatesBySeries.Values.ToArray();

        var dialysisScore = ScoreNoValidDosesPatientSeries.Execute(candidatesBySeries["HepB risk Dialysis 4-dose series"], allCandidates);
        var sixtyYearGroupScore = ScoreNoValidDosesPatientSeries.Execute(candidatesBySeries["HepB risk 3-dose series"], allCandidates);

        // Dialysis ties with Recombivax for earliest (condition 1 = 0), while the 60-year group
        // series is not earliest at all (condition 1 = -1) - exactly a 1-point gap, since
        // conditions 2 and 3 are identical between these two specific series (both completable,
        // both non-product).
        Assert.Equal(1, dialysisScore - sixtyYearGroupScore);
    }
}
