/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Xunit;

namespace Cdsi.Core.Tests;

public class DetermineForecastDoseNumberTests
{
    [Fact]
    public void NoSatisfiedDoses_ForecastsDoseOne()
    {
        var result = DetermineForecastDoseNumber.Execute(Array.Empty<SatisfiedTargetDoseInfo>());

        Assert.Equal(1, result);
    }

    [Fact]
    public void AllNonSeasonalSatisfiedDoses_AllCount()
    {
        var doses = new[]
        {
            new SatisfiedTargetDoseInfo(new DateOnly(2020, 1, 1), null),
            new SatisfiedTargetDoseInfo(new DateOnly(2020, 3, 1), null)
        };

        var result = DetermineForecastDoseNumber.Execute(doses);

        Assert.Equal(3, result); // 2 satisfied + 1
    }

    [Fact]
    public void SeasonalDoseGivenBeforeSeasonStart_DoesNotCount()
    {
        var doses = new[]
        {
            new SatisfiedTargetDoseInfo(new DateOnly(2020, 1, 1), null), // non-seasonal, counts
            new SatisfiedTargetDoseInfo(new DateOnly(2024, 6, 1), new DateOnly(2024, 7, 1)) // given BEFORE season opened
        };

        var result = DetermineForecastDoseNumber.Execute(doses);

        Assert.Equal(2, result); // only 1 qualifying dose + 1
    }

    [Fact]
    public void SeasonalDoseGivenOnOrAfterSeasonStart_Counts()
    {
        var doses = new[]
        {
            new SatisfiedTargetDoseInfo(new DateOnly(2024, 7, 1), new DateOnly(2024, 7, 1)) // exactly at season start
        };

        var result = DetermineForecastDoseNumber.Execute(doses);

        Assert.Equal(2, result); // 1 qualifying + 1
    }
}
