/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Common;
using Xunit;

namespace OpenCdsi.VaxEngine.Core.Tests;

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
    public void MonthsAddition_WhenTargetDayDoesNotExist_RollsToFirstOfFollowingMonth_SpecExample1()
    {
        // REAL BUG, FOUND AND FIXED - see AddMonthsWithRollover's own doc comment for the full
        // derivation. CALCDT-5's own first worked example, verbatim: "03/31/2000 + 6 months =
        // 10/01/2000 (September 31 does not exist)". .NET's own DateOnly.AddMonths would clamp
        // to 09/30/2000 instead - this asserts the spec's own literal answer, not .NET's default.
        var d = DurationExpression.Parse("6 months");
        Assert.Equal(new DateOnly(2000, 10, 1), d.AddTo(new DateOnly(2000, 3, 31)));
    }

    [Fact]
    public void MonthsAddition_WhenTargetDayDoesNotExist_RollsToFirstOfFollowingMonth_SpecExample2()
    {
        // CALCDT-5's own second worked example, verbatim: "08/31/2010 + 6 months = 03/01/2011
        // (February 31 does not exist)" - also confirms the rollover crosses a year boundary
        // correctly (February 2011, not a leap year, only has 28 days).
        var d = DurationExpression.Parse("6 months");
        Assert.Equal(new DateOnly(2011, 3, 1), d.AddTo(new DateOnly(2010, 8, 31)));
    }

    [Fact]
    public void MonthsAddition_WhenTargetDayDoesNotExist_RollsToFirstOfFollowingMonth_RealCorpusCase()
    {
        // Real corpus cases 2013-0003/2013-0130/2013-0165 (DTaP-family, DOB 2026-05-31): the
        // real bug this fix addresses, reconstructed directly - "05/31/2026 + 6 months" naively
        // clamps to 11/30/2026 (November only has 30 days), but CALCDT-5 says it should roll to
        // 12/01/2026 - exactly the real corpus's own expected recommendedDate.
        var d = DurationExpression.Parse("6 months");
        Assert.Equal(new DateOnly(2026, 12, 1), d.AddTo(new DateOnly(2026, 5, 31)));
    }

    [Fact]
    public void MonthsAddition_WhenTargetDayDoesExist_NoRollover()
    {
        // Regression guard: confirms the rollover logic doesn't fire when it shouldn't - a
        // genuinely valid target date (day 30 exists in every month) is returned as-is, not
        // pushed forward to the 1st of the next month.
        var d = DurationExpression.Parse("1 month");
        Assert.Equal(new DateOnly(2024, 2, 29), d.AddTo(new DateOnly(2024, 1, 29))); // 2024 is a leap year - Feb 29 exists
        Assert.Equal(new DateOnly(2024, 4, 30), d.AddTo(new DateOnly(2024, 3, 30)));
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
