/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluateAgeTests
{
    private static readonly IReadOnlyList<AntigenSeries> HpvSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HPV-508.xml"));

    private static readonly IReadOnlyList<AntigenSeries> CovidSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_COVID-19-508.xml"));

    // Real data: HPV 2-dose series, Dose 1 — absMinAge "9 years - 4 days", minAge "9 years",
    // maxAge "46 years", single unversioned age rule. Exercises all four Table 6-15 branches
    // with one fixture.
    private static IReadOnlyList<AgeRule> HpvDose1AgeRules =>
        HpvSeries.Single(s => s.SeriesName == "HPV 2-dose series").SeriesDoses.Single(d => d.DoseNumber == 1).AgeRules;

    // Real data: "COVID-19 start at 6mo-23mo shared clinical decision-making series", Dose 1 —
    // the two-window age rule (pre/post 2023-09-12) we traced by hand earlier in the design
    // session. Several COVID-19 series share this exact pair of rules; naming one explicitly
    // avoids an ambiguous match.
    private static IReadOnlyList<AgeRule> Covid19Dose1AgeRules =>
        CovidSeries.Single(s => s.SeriesName == "COVID-19 start at 6mo-23mo shared clinical decision-making series")
            .SeriesDoses.Single(d => d.DoseNumber == 1).AgeRules;

    private static readonly DateOnly Dob = new(2014, 1, 1);

    [Fact]
    public void TooYoung_BeforeAbsoluteMinimumAge_IsNotValid()
    {
        // absMinAgeDate = 2014-01-01 + (9y - 4d) = 2022-12-28
        var result = EvaluateAge.Execute(Dob, new DateOnly(2022, 12, 1), HpvDose1AgeRules);

        Assert.False(result.IsValid);
        Assert.False(result.IsExtraneous);
        Assert.Equal("Too young", result.Reason);
    }

    [Fact]
    public void GracePeriod_BetweenAbsoluteMinimumAndMinimumAge_IsValidWithGracePeriodReason()
    {
        // absMinAgeDate = 2022-12-28, minAgeDate = 2023-01-01 (9 years exactly).
        var result = EvaluateAge.Execute(Dob, new DateOnly(2022, 12, 30), HpvDose1AgeRules);

        Assert.True(result.IsValid);
        Assert.False(result.IsExtraneous);
        Assert.Equal("Grace period", result.Reason);
    }

    [Fact]
    public void Valid_BetweenMinimumAndMaximumAge_IsValidWithNoSpecialReason()
    {
        // minAgeDate = 2023-01-01, maxAgeDate = 2060-01-01 (46 years).
        var result = EvaluateAge.Execute(Dob, new DateOnly(2025, 6, 1), HpvDose1AgeRules);

        Assert.True(result.IsValid);
        Assert.False(result.IsExtraneous);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void TooOld_AtOrAfterMaximumAge_IsNotValidAndExtraneous()
    {
        // maxAgeDate = 2014-01-01 + 46 years = 2060-01-01.
        var result = EvaluateAge.Execute(Dob, new DateOnly(2060, 1, 1), HpvDose1AgeRules);

        Assert.False(result.IsValid);
        Assert.True(result.IsExtraneous);
        Assert.Equal("Too old", result.Reason);
    }

    [Fact]
    public void NoAgeRulesSpecified_IsAlwaysValid()
    {
        // §6.4: "In cases where a target dose does not specify age attributes, the age at
        // administration is considered 'valid.'" No real seriesDose in the current data
        // actually omits <age> entirely, so this exercises the documented default directly
        // rather than via a real fixture.
        var result = EvaluateAge.Execute(Dob, new DateOnly(2020, 1, 1), Array.Empty<AgeRule>());

        Assert.True(result.IsValid);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void TemporallyVersionedAgeRule_SelectsApplicableWindowByDateAdministered()
    {
        // Real COVID-19 Dose 1 data we traced by hand earlier in the design session:
        // pre-2023-09-12 window has absMinAge "0 days"/minAge "6 months";
        // on/after 2023-09-12 window has absMinAge "6 months - 4 days"/minAge "6 months".
        var dob = new DateOnly(2023, 1, 15);

        // 2023-09-05 falls in the OLD window (cessation 2023-09-11): absMinAgeDate = dob (0 days).
        var beforeCutover = EvaluateAge.Execute(dob, new DateOnly(2023, 9, 5), Covid19Dose1AgeRules);
        Assert.True(beforeCutover.IsValid);

        // 2023-09-20 falls in the NEW window (effective 2023-09-12):
        // absMinAgeDate = 2023-07-11, minAgeDate = 2023-07-15 -> well past both -> plain Valid.
        var afterCutover = EvaluateAge.Execute(dob, new DateOnly(2023, 9, 20), Covid19Dose1AgeRules);
        Assert.True(afterCutover.IsValid);
        Assert.Null(afterCutover.Reason);
    }
}
