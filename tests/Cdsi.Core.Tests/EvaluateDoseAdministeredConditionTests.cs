/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluateDoseAdministeredConditionTests
{
    [Fact]
    public void NormalDose_CanBeEvaluated()
    {
        var result = EvaluateDoseAdministeredCondition.Execute(
            new DateOnly(2024, 1, 1), lotExpirationDate: new DateOnly(2025, 1, 1), doseConditionFlag: false);

        Assert.True(result.CanBeEvaluated);
    }

    [Fact]
    public void AdministeredAfterLotExpiration_CannotBeEvaluated()
    {
        var result = EvaluateDoseAdministeredCondition.Execute(
            new DateOnly(2025, 6, 1), lotExpirationDate: new DateOnly(2025, 1, 1), doseConditionFlag: false);

        Assert.False(result.CanBeEvaluated);
        Assert.Equal("Administered after lot expiration date", result.Reason);
    }

    [Fact]
    public void DoseConditionFlagSet_CannotBeEvaluated_EvenIfNotExpired()
    {
        var result = EvaluateDoseAdministeredCondition.Execute(
            new DateOnly(2024, 1, 1), lotExpirationDate: new DateOnly(2025, 1, 1), doseConditionFlag: true);

        Assert.False(result.CanBeEvaluated);
        Assert.Equal("Dose condition flag is set", result.Reason);
    }

    [Fact]
    public void NoLotExpirationDateKnown_DefaultsToNeverExpired()
    {
        // Table 6-2: assumed value if empty is 12/31/2999.
        var result = EvaluateDoseAdministeredCondition.Execute(
            new DateOnly(2024, 1, 1), lotExpirationDate: null, doseConditionFlag: false);

        Assert.True(result.CanBeEvaluated);
    }
}
