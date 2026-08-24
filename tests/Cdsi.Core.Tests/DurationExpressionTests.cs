/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Common;
using Xunit;

namespace Cdsi.Core.Tests;

public class DurationExpressionTests
{
    [Fact]
    public void SimpleMonths_AddsCalendarMonths()
    {
        var d = DurationExpression.Parse("6 months");
        Assert.Equal(new DateOnly(2024, 7, 1), d.AddTo(new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public void SimpleWeeks_AddsSevenDaysPerWeek()
    {
        var d = DurationExpression.Parse("8 weeks");
        Assert.Equal(new DateOnly(2024, 2, 26), d.AddTo(new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public void CompoundExpression_SubtractsDaysAfterAddingPrimaryUnit()
    {
        // Real HepB dose-3 data: "8 weeks - 4 days"
        var d = DurationExpression.Parse("8 weeks - 4 days");
        // 2024-01-01 + 8 weeks (56 days) = 2024-02-26; minus 4 days = 2024-02-22
        Assert.Equal(new DateOnly(2024, 2, 22), d.AddTo(new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public void MonthsMinusDays_AddsCalendarMonthsThenSubtractsDays()
    {
        // Real COVID-19 dose-1 data: "6 months - 4 days"
        var d = DurationExpression.Parse("6 months - 4 days");
        // 2023-01-15 + 6 months = 2023-07-15; minus 4 days = 2023-07-11
        Assert.Equal(new DateOnly(2023, 7, 11), d.AddTo(new DateOnly(2023, 1, 15)));
    }

    [Fact]
    public void MonthsPlusDays_AddsCalendarMonthsThenAddsDays()
    {
        // Real Rotavirus dose data: "8 months + 1 day"
        var d = DurationExpression.Parse("8 months + 1 day");
        // 2024-01-01 + 8 months = 2024-09-01; plus 1 day = 2024-09-02
        Assert.Equal(new DateOnly(2024, 9, 2), d.AddTo(new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public void ZeroDays_ReturnsAnchorUnchanged()
    {
        var d = DurationExpression.Parse("0 days");
        Assert.Equal(new DateOnly(2024, 1, 1), d.AddTo(new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public void Years_AddsCalendarYears()
    {
        var d = DurationExpression.Parse("50 years");
        Assert.Equal(new DateOnly(2074, 1, 1), d.AddTo(new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public void TryParse_EmptyOrWhitespace_ReturnsFalse()
    {
        Assert.False(DurationExpression.TryParse(null, out _));
        Assert.False(DurationExpression.TryParse("", out _));
        Assert.False(DurationExpression.TryParse("   ", out _));
    }

    [Fact]
    public void Parse_UnrecognizedFormat_Throws()
    {
        Assert.Throws<FormatException>(() => DurationExpression.Parse("six months"));
    }
}
