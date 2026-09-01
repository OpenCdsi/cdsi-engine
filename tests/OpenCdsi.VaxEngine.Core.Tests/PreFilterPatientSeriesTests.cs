/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.ReferenceData;
using Xunit;

namespace OpenCdsi.VaxEngine.Core.Tests;

public class PreFilterPatientSeriesTests
{
    // Real data: HepB series group "2" ("Increased Risk") has 8 Risk-type series with a genuine
    // priority mix - 6 at "B", 2 at "A" (Dialysis, Recombivax).
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepB"));

    private static IReadOnlyList<AntigenSeries> Group2RiskSeries =>
        HepBSeries.Where(s => s.SeriesGroupInfo.SeriesGroup == "2").ToArray();

    private static AntigenSeries HepB3Dose => HepBSeries.Single(s => s.SeriesName == "HepB 3-dose series");

    private static readonly AntigenSeries HepAEvaluationOnly =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepA"))
            .Single(s => s.SeriesName == "HepA risk Twinrix tertiary 3-dose series");

    private static ScorableSeriesCandidate MakeCandidate(AntigenSeries series, bool contraindicated = false,
        PatientSeriesStatus status = PatientSeriesStatus.NotComplete, int validDoses = 0, DateOnly? earliestValidDate = null) =>
        new(series, contraindicated, status, validDoses, earliestValidDate);

    [Fact]
    public void CandidateScorable_NotContraindicated_IsAlwaysCandidate()
    {
        var result = PreFilterPatientSeries.IsCandidateScorablePatientSeries(false, new[] { false, true });
        Assert.True(result);
    }

    [Fact]
    public void CandidateScorable_Contraindicated_ButOthersInGroupAreNot_IsNotCandidate()
    {
        var result = PreFilterPatientSeries.IsCandidateScorablePatientSeries(true, new[] { true, false, true });
        Assert.False(result);
    }

    [Fact]
    public void CandidateScorable_Contraindicated_AllOthersAlsoContraindicated_IsCandidate()
    {
        var result = PreFilterPatientSeries.IsCandidateScorablePatientSeries(true, new[] { true, true, true });
        Assert.True(result);
    }

    [Fact]
    public void Bullet1_RiskSeries_RealHepBPriorityMix_OnlyHighestPriorityQualify()
    {
        var candidates = Group2RiskSeries.Select(s => MakeCandidate(s)).ToArray();
        var dob = new DateOnly(1990, 1, 1);

        var results = candidates.ToDictionary(
            c => c.Series.SeriesName,
            c => PreFilterPatientSeries.IsScorablePatientSeries(c, isCandidateScorable: true, candidates, dob));

        Assert.True(results["HepB risk Dialysis 4-dose series"]);   // priority A
        Assert.True(results["HepB risk Recombivax 3-dose series"]); // priority A
        Assert.False(results["HepB risk 3-dose series"]);           // priority B
        Assert.False(results["HepB risk Heplisav-B 2-dose series"]); // priority B
    }

    [Fact]
    public void Bullet2_StandardSeries_ValidDoseBeforeMaxAgeToStart_IsScorable()
    {
        // HepB 3-dose series maxAgeToStart is "19 years".
        var dob = new DateOnly(2000, 1, 1);
        var candidate = MakeCandidate(HepB3Dose, validDoses: 1, earliestValidDate: new DateOnly(2015, 1, 1)); // age 15

        var result = PreFilterPatientSeries.IsScorablePatientSeries(candidate, isCandidateScorable: true, new[] { candidate }, dob);

        Assert.True(result);
    }

    [Fact]
    public void Bullet2_StandardSeries_ValidDoseAfterMaxAgeToStart_IsNotScorable()
    {
        var dob = new DateOnly(2000, 1, 1);
        // Age 20 at "valid dose" date - past the 19-year maxAgeToStart.
        var candidate = MakeCandidate(HepB3Dose, validDoses: 1, earliestValidDate: new DateOnly(2020, 1, 1));

        var result = PreFilterPatientSeries.IsScorablePatientSeries(candidate, isCandidateScorable: true, new[] { candidate }, dob);

        Assert.False(result);
    }

    [Fact]
    public void Bullet3_AllZeroValidDosesInGroup_NoDefaultSeries_AllQualify()
    {
        var dob = new DateOnly(2000, 1, 1);
        // Use two non-default series from group 1 (all HepB group-1 series checked earlier are
        // "No" for defaultSeries except "HepB 3-dose series" itself, which IS the default -
        // use two of the other nine instead).
        var seriesA = HepBSeries.Single(s => s.SeriesName == "HepB 4-dose series");
        var seriesB = HepBSeries.Single(s => s.SeriesName == "HepB adolescent 2-dose series");
        var candidates = new[] { MakeCandidate(seriesA, validDoses: 0), MakeCandidate(seriesB, validDoses: 0) };

        var result = PreFilterPatientSeries.IsScorablePatientSeries(candidates[0], isCandidateScorable: true, candidates, dob);

        Assert.True(result);
    }

    [Fact]
    public void Bullet3_DefaultSeriesExistsInGroup_DisqualifiesTheAllZeroValidDosesPath()
    {
        var dob = new DateOnly(2000, 1, 1);
        var defaultSeries = HepB3Dose; // real data: this IS the default series for group 1
        var otherSeries = HepBSeries.Single(s => s.SeriesName == "HepB 4-dose series");
        var candidates = new[] { MakeCandidate(defaultSeries, validDoses: 0), MakeCandidate(otherSeries, validDoses: 0) };

        var result = PreFilterPatientSeries.IsScorablePatientSeries(candidates[1], isCandidateScorable: true, candidates, dob);

        Assert.False(result);
    }

    [Fact]
    public void Bullet4_EvaluationOnlySeries_Complete_IsScorable_EvenWhenNotCandidateScorable()
    {
        var dob = new DateOnly(2000, 1, 1);
        var candidate = MakeCandidate(HepAEvaluationOnly, status: PatientSeriesStatus.Complete);

        // isCandidateScorable: false - bullet 4 has no such requirement, unlike bullets 1-3.
        var result = PreFilterPatientSeries.IsScorablePatientSeries(candidate, isCandidateScorable: false, new[] { candidate }, dob);

        Assert.True(result);
    }

    [Fact]
    public void NotCandidateScorable_StandardOrRiskSeries_IsNeverScorable()
    {
        var dob = new DateOnly(2000, 1, 1);
        var candidate = MakeCandidate(HepB3Dose, validDoses: 1, earliestValidDate: new DateOnly(2015, 1, 1));

        var result = PreFilterPatientSeries.IsScorablePatientSeries(candidate, isCandidateScorable: false, new[] { candidate }, dob);

        Assert.False(result);
    }
}
