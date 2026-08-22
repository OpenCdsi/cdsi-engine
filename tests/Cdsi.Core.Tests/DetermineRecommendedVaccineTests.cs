using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class DetermineRecommendedVaccineTests
{
    // Real data: "HepB adolescent 2-dose series" Dose 1 - CVX 43, forecastVaccineType "Y",
    // no age gate at all (unbounded).
    private static PreferableVaccine HepBUnbounded =>
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"))
            .Single(s => s.SeriesName == "HepB adolescent 2-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 1).PreferableVaccines.Single(pv => pv.Cvx == "43");

    // Real data: "Dengue risk 3-dose series" Dose 1 - CVX 56, forecastVaccineType "N".
    private static PreferableVaccine DengueNotForecastEligible =>
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Dengue-508.xml"))
            .Single(s => s.SeriesName == "Dengue risk 3-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 1).PreferableVaccines.Single(pv => pv.Cvx == "56");

    // Real data: MenB-4C Shared Clinical Decision Making Dose 3 - CVX 328, forecastVaccineType
    // "Y", real age window [10 years, 26 years). This dose has 2 preferableVaccine entries in
    // real data, so filter by CVX explicitly rather than assuming a single entry.
    private static PreferableVaccine MenBAgeGated =>
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Meningococcal_B-508.xml"))
            .Single(s => s.SeriesName == "Meningococcal B 3-dose series MenB-4C Shared Clinical Decision Making")
            .SeriesDoses.Single(d => d.DoseNumber == 3).PreferableVaccines.Single(pv => pv.Cvx == "328");

    [Fact]
    public void ForecastVaccineTypeFlagN_NeverRecommended_RegardlessOfEverythingElse()
    {
        var dob = new DateOnly(2000, 1, 1);
        var result = DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine(
            DengueNotForecastEligible, isVaccineTypeContraindicated: false, dob,
            earliestDate: new DateOnly(2024, 1, 1), adjustedRecommendedDate: new DateOnly(2024, 1, 1));

        Assert.False(result);
    }

    [Fact]
    public void ContraindicatedVaccineType_NeverRecommended_EvenWithFlagYAndValidWindow()
    {
        var dob = new DateOnly(2000, 1, 1);
        var result = DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine(
            HepBUnbounded, isVaccineTypeContraindicated: true, dob,
            earliestDate: new DateOnly(2024, 1, 1), adjustedRecommendedDate: new DateOnly(2024, 1, 1));

        Assert.False(result);
    }

    [Fact]
    public void UnboundedAgeWindow_FlagY_NotContraindicated_IsRecommended()
    {
        var dob = new DateOnly(2000, 1, 1);
        var result = DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine(
            HepBUnbounded, isVaccineTypeContraindicated: false, dob,
            earliestDate: new DateOnly(2024, 1, 1), adjustedRecommendedDate: new DateOnly(2024, 6, 1));

        Assert.True(result);
    }

    [Fact]
    public void AgeGatedVaccine_EarliestDateWithinWindow_IsRecommended()
    {
        var dob = new DateOnly(2000, 1, 1);
        // Window: [2010-01-01, 2026-01-01). Earliest date within, adjusted recommended date not.
        var result = DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine(
            MenBAgeGated, isVaccineTypeContraindicated: false, dob,
            earliestDate: new DateOnly(2015, 1, 1), adjustedRecommendedDate: new DateOnly(2030, 1, 1));

        Assert.True(result); // "at least one of" - earliest date alone is sufficient
    }

    [Fact]
    public void AgeGatedVaccine_OnlyAdjustedRecommendedDateWithinWindow_IsRecommended()
    {
        var dob = new DateOnly(2000, 1, 1);
        var result = DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine(
            MenBAgeGated, isVaccineTypeContraindicated: false, dob,
            earliestDate: new DateOnly(1990, 1, 1), adjustedRecommendedDate: new DateOnly(2015, 1, 1));

        Assert.True(result);
    }

    [Fact]
    public void AgeGatedVaccine_NeitherDateWithinWindow_NotRecommended()
    {
        var dob = new DateOnly(2000, 1, 1);
        // Both dates before the window opens (2010-01-01).
        var result = DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine(
            MenBAgeGated, isVaccineTypeContraindicated: false, dob,
            earliestDate: new DateOnly(2005, 1, 1), adjustedRecommendedDate: new DateOnly(2008, 1, 1));

        Assert.False(result);
    }

    [Fact]
    public void AgeGatedVaccine_AtExactBeginBoundary_IsRecommended()
    {
        var dob = new DateOnly(2000, 1, 1);
        // Window begins exactly 2010-01-01 (dob + 10 years) - boundary is inclusive.
        var result = DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine(
            MenBAgeGated, isVaccineTypeContraindicated: false, dob,
            earliestDate: new DateOnly(2010, 1, 1), adjustedRecommendedDate: new DateOnly(2010, 1, 1));

        Assert.True(result);
    }

    [Fact]
    public void AgeGatedVaccine_AtExactEndBoundary_NotRecommended()
    {
        var dob = new DateOnly(2000, 1, 1);
        // Window ends exactly 2026-01-01 (dob + 26 years) - boundary is exclusive.
        var result = DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine(
            MenBAgeGated, isVaccineTypeContraindicated: false, dob,
            earliestDate: new DateOnly(2026, 1, 1), adjustedRecommendedDate: new DateOnly(2026, 1, 1));

        Assert.False(result);
    }
}
