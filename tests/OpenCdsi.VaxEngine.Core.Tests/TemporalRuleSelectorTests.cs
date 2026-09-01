/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Common;
using Xunit;

namespace OpenCdsi.VaxEngine.Core.Tests;

public class TemporalRuleSelectorTests
{
    private sealed record FakeRule(DateOnly? EffectiveDate, DateOnly? CessationDate, string Label) : ITemporallyVersioned;

    [Fact]
    public void SelectsInstanceWhoseWindowContainsAnchorDate()
    {
        // Mirrors the real COVID-19 Dose 1 age-rule split we found: pre-2023-09-12 vs on/after.
        var instances = new[]
        {
            new FakeRule(null, new DateOnly(2023, 9, 12), "old"), // cessation 2023-09-12
            new FakeRule(new DateOnly(2023, 9, 12), null, "new")
        };

        var beforeCutover = TemporalRuleSelector.SelectApplicable(instances, new DateOnly(2023, 9, 5));
        var atCutover = TemporalRuleSelector.SelectApplicable(instances, new DateOnly(2023, 9, 12));
        var afterCutover = TemporalRuleSelector.SelectApplicable(instances, new DateOnly(2023, 12, 1));

        Assert.Equal("old", beforeCutover.Label);
        Assert.Equal("new", atCutover.Label); // effectiveDate is inclusive
        Assert.Equal("new", afterCutover.Label);
    }

    [Fact]
    public void NoApplicableInstance_Throws()
    {
        var instances = new[]
        {
            new FakeRule(new DateOnly(2025, 1, 1), null, "future-only")
        };

        Assert.Throws<InvalidOperationException>(() =>
            TemporalRuleSelector.SelectApplicable(instances, new DateOnly(2020, 1, 1)));
    }

    [Fact]
    public void OrDefault_ReturnsNullInsteadOfThrowing_WhenNoneApply()
    {
        var instances = new[]
        {
            new FakeRule(new DateOnly(2025, 1, 1), null, "future-only")
        };

        var result = TemporalRuleSelector.SelectApplicableOrDefault(instances, new DateOnly(2020, 1, 1));
        Assert.Null(result);
    }

    [Fact]
    public void OrDefault_EmptyList_ReturnsNull()
    {
        var result = TemporalRuleSelector.SelectApplicableOrDefault(Array.Empty<FakeRule>(), new DateOnly(2020, 1, 1));
        Assert.Null(result);
    }
}
