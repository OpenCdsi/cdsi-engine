/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluatePreferableVaccineTests
{
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    private static readonly IReadOnlyList<AntigenSeries> DengueSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Dengue-508.xml"));

    // Real data: "HepB adolescent 2-dose series" Dose 1 - single preferable vaccine,
    // CVX 43, tradeName "RECOMBIVAX ADULT", volume 1.0, no age bounds.
    private static IReadOnlyList<PreferableVaccine> HepBAdolescentDose1 =>
        HepBSeries.Single(s => s.SeriesName == "HepB adolescent 2-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 1).PreferableVaccines;

    // Real data: "Dengue risk 3-dose series" Dose 1 - CVX 56, beginAge 9 years, endAge 17 years,
    // no trade name, volume 0.5.
    private static IReadOnlyList<PreferableVaccine> DengueRiskDose1 =>
        DengueSeries.Single(s => s.SeriesName == "Dengue risk 3-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 1).PreferableVaccines;

    private static VaccineDoseAdministered MakeDose(string cvx, DateOnly dateAdministered, string? tradeName = null, double? volume = null) =>
        new() { DoseId = "d1", Cvx = cvx, DateAdministered = dateAdministered, TradeName = tradeName, Volume = volume };

    [Fact]
    public void ExactMatch_CvxTradeNameAndVolumeAllSatisfied_IsValidWithNoReason()
    {
        var dose = MakeDose("43", new DateOnly(2020, 1, 1), tradeName: "RECOMBIVAX ADULT", volume: 1.0);

        var result = EvaluatePreferableVaccine.Execute(new DateOnly(2005, 1, 1), dose, HepBAdolescentDose1);

        Assert.True(result.IsValid);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void WrongCvx_IsNotValid()
    {
        var dose = MakeDose("08", new DateOnly(2020, 1, 1), tradeName: "RECOMBIVAX ADULT", volume: 1.0);

        var result = EvaluatePreferableVaccine.Execute(new DateOnly(2005, 1, 1), dose, HepBAdolescentDose1);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void WrongTradeName_IsNotValid()
    {
        var dose = MakeDose("43", new DateOnly(2020, 1, 1), tradeName: "ENGERIX-B", volume: 1.0);

        var result = EvaluatePreferableVaccine.Execute(new DateOnly(2005, 1, 1), dose, HepBAdolescentDose1);

        Assert.False(result.IsValid);
        Assert.Contains("trade name", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InsufficientVolume_IsSTILLValid_ButWithVolumeReason()
    {
        // The exact Table 6-26 nuance: volume failing does NOT flip Yes to No.
        var dose = MakeDose("43", new DateOnly(2020, 1, 1), tradeName: "RECOMBIVAX ADULT", volume: 0.5);

        var result = EvaluatePreferableVaccine.Execute(new DateOnly(2005, 1, 1), dose, HepBAdolescentDose1);

        Assert.True(result.IsValid);
        Assert.Equal("Volume administered is less than recommended volume", result.Reason);
    }

    [Fact]
    public void NoTradeNameSpecifiedOnPreferableVaccine_SkipsTradeNameCheck()
    {
        // Dengue's preferable vaccine entry has no tradeName - any administered trade name (or none) should pass that condition.
        var dose = MakeDose("56", new DateOnly(2015, 1, 1), tradeName: "Dengvaxia", volume: 0.5);

        var result = EvaluatePreferableVaccine.Execute(new DateOnly(2005, 1, 1), dose, DengueRiskDose1); // age 10 - within [9y, 17y)

        Assert.True(result.IsValid);
    }

    [Fact]
    public void OutsideAgeWindow_IsNotValidWithAgeReason()
    {
        // Dengue: beginAge 9 years, endAge 17 years. DOB 2005-01-01, administered 2012-01-01 -> age 7, before the window.
        var dose = MakeDose("56", new DateOnly(2012, 1, 1), volume: 0.5);

        var result = EvaluatePreferableVaccine.Execute(new DateOnly(2005, 1, 1), dose, DengueRiskDose1);

        Assert.False(result.IsValid);
        Assert.Contains("age range", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoPreferableVaccinesDefined_IsAlwaysValid()
    {
        var dose = MakeDose("999", new DateOnly(2020, 1, 1));

        var result = EvaluatePreferableVaccine.Execute(new DateOnly(2005, 1, 1), dose, Array.Empty<PreferableVaccine>());

        Assert.True(result.IsValid);
    }
}
