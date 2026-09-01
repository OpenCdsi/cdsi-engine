/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.ReferenceData;
using Xunit;

namespace OpenCdsi.VaxEngine.Core.Tests;

public class MultipleAntigenVaccineGroupTests
{
    // Real data: "Pertussis standard series" Dose 2 (Pertussis belongs to the real multi-antigen
    // "DTaP/Tdap/Td" group) has exactly one preferable interval, intervalPriority "override".
    private static IReadOnlyList<PreferableIntervalRule> PertussisDose2Intervals =>
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("Pertussis"))
            .Single(s => s.SeriesName == "Pertussis standard series")
            .SeriesDoses.Single(d => d.DoseNumber == 2).PreferableIntervals;

    // Real data: "HepB 3-dose series" Dose 3's fromPrevious interval has no intervalPriority at all.
    private static IReadOnlyList<PreferableIntervalRule> HepBDose3Intervals =>
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepB"))
            .Single(s => s.SeriesName == "HepB 3-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 3).PreferableIntervals;

    [Fact]
    public void Status_ContraindicatedDominatesEverythingElse()
    {
        var statuses = new[] { PatientSeriesStatus.Contraindicated, PatientSeriesStatus.Complete, PatientSeriesStatus.Immune, PatientSeriesStatus.AgedOut };
        Assert.Equal(PatientSeriesStatus.Contraindicated, MultipleAntigenVaccineGroup.Status(statuses));
    }

    [Fact]
    public void Status_AgedOutWinsWhenNoContraindicated()
    {
        var statuses = new[] { PatientSeriesStatus.AgedOut, PatientSeriesStatus.NotRecommended, PatientSeriesStatus.Complete };
        Assert.Equal(PatientSeriesStatus.AgedOut, MultipleAntigenVaccineGroup.Status(statuses));
    }

    [Fact]
    public void Status_NotRecommendedWinsWhenNoContraindicatedOrAgedOut()
    {
        var statuses = new[] { PatientSeriesStatus.NotRecommended, PatientSeriesStatus.NotComplete, PatientSeriesStatus.Complete };
        Assert.Equal(PatientSeriesStatus.NotRecommended, MultipleAntigenVaccineGroup.Status(statuses));
    }

    [Fact]
    public void Status_NotCompleteWinsWhenOnlyImmuneAndCompleteRemain()
    {
        var statuses = new[] { PatientSeriesStatus.NotComplete, PatientSeriesStatus.Complete, PatientSeriesStatus.Immune };
        Assert.Equal(PatientSeriesStatus.NotComplete, MultipleAntigenVaccineGroup.Status(statuses));
    }

    [Fact]
    public void Status_AllImmune_ResultsInImmune()
    {
        var statuses = new[] { PatientSeriesStatus.Immune, PatientSeriesStatus.Immune };
        Assert.Equal(PatientSeriesStatus.Immune, MultipleAntigenVaccineGroup.Status(statuses));
    }

    [Fact]
    public void Status_MixOfCompleteAndImmune_ResultsInComplete_NotImmune()
    {
        // Not ALL Immune (one is Complete), and nothing else dominates - falls to the final
        // "Complete" outcome rather than Immune.
        var statuses = new[] { PatientSeriesStatus.Complete, PatientSeriesStatus.Immune };
        Assert.Equal(PatientSeriesStatus.Complete, MultipleAntigenVaccineGroup.Status(statuses));
    }

    [Fact]
    public void Status_AllComplete_ResultsInComplete()
    {
        var statuses = new[] { PatientSeriesStatus.Complete, PatientSeriesStatus.Complete };
        Assert.Equal(PatientSeriesStatus.Complete, MultipleAntigenVaccineGroup.Status(statuses));
    }

    [Fact]
    public void IsPriorityForecast_RealPertussisDose2_AllIntervalsHaveOverrideFlag_IsPriority()
    {
        var result = MultipleAntigenVaccineGroup.IsPriorityPatientSeriesForecast(PertussisDose2Intervals);
        Assert.True(result);
    }

    [Fact]
    public void IsPriorityForecast_RealHepBDose3_NoPriorityFlag_IsNotPriority()
    {
        // fromPrevious group alone (no override flag on it in real data).
        var fromPrevious = HepBDose3Intervals.Where(iv => iv.ReferenceType == IntervalReferenceType.FromPrevious).ToArray();
        var result = MultipleAntigenVaccineGroup.IsPriorityPatientSeriesForecast(fromPrevious);
        Assert.False(result);
    }

    [Fact]
    public void IsPriorityForecast_NoIntervalsAtAll_IsNotPriority()
    {
        var result = MultipleAntigenVaccineGroup.IsPriorityPatientSeriesForecast(Array.Empty<PreferableIntervalRule>());
        Assert.False(result);
    }

    [Fact]
    public void IsPriorityForecast_MixedFlags_RequiresEveryIntervalToHaveIt()
    {
        // Synthetic - real data never has a mixed case (confirmed by sweeping all 30 files), but
        // FORECASTPRIORITY-1's "each" wording still needs to be enforced correctly if one ever appears.
        var withFlag = PertussisDose2Intervals.Single();
        var withoutFlag = HepBDose3Intervals.Single(iv => iv.ReferenceType == IntervalReferenceType.FromPrevious);

        var result = MultipleAntigenVaccineGroup.IsPriorityPatientSeriesForecast(new[] { withFlag, withoutFlag });

        Assert.False(result);
    }

    [Fact]
    public void EarliestDate_NoPriorityForecast_TakesLatestOfContained()
    {
        var contained = new[] { new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 1) };

        var result = MultipleAntigenVaccineGroup.EarliestDate(false, contained, latestAdministeredDateOfGroupVaccineTypes: null);

        Assert.Equal(new DateOnly(2024, 6, 1), result);
    }

    [Fact]
    public void EarliestDate_PriorityForecast_NoAdministeredHistory_TakesEarliestOfContained()
    {
        var contained = new[] { new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 1) };

        var result = MultipleAntigenVaccineGroup.EarliestDate(true, contained, latestAdministeredDateOfGroupVaccineTypes: null);

        Assert.Equal(new DateOnly(2024, 1, 1), result);
    }

    [Fact]
    public void EarliestDate_PriorityForecast_AdministeredHistoryLaterThanContained_AdministeredDateWins()
    {
        var contained = new[] { new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 1) };

        var result = MultipleAntigenVaccineGroup.EarliestDate(true, contained, latestAdministeredDateOfGroupVaccineTypes: new DateOnly(2024, 3, 1));

        Assert.Equal(new DateOnly(2024, 3, 1), result);
    }

    [Fact]
    public void EarliestDate_PriorityForecast_AdministeredHistoryEarlierThanContained_ContainedWins()
    {
        var contained = new[] { new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 1) };

        var result = MultipleAntigenVaccineGroup.EarliestDate(true, contained, latestAdministeredDateOfGroupVaccineTypes: new DateOnly(2020, 1, 1));

        Assert.Equal(new DateOnly(2024, 1, 1), result);
    }
}
