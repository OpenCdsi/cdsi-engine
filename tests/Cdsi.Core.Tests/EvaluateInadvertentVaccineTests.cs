/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluateInadvertentVaccineTests
{
    private static readonly IReadOnlyList<AntigenSeries> PolioSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("Polio"));

    // Real data: "Polio 4-dose series" Dose 1 lists three inadvertent CVX codes -
    // 178 (OPV bivalent), 179 (OPV monovalent unspecified), 182 (OPV unspecified).
    private static IReadOnlyList<string> PolioDose1InadvertentCvxCodes =>
        PolioSeries.Single(s => s.SeriesName == "Polio 4-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 1).InadvertentVaccineCvxCodes;

    [Fact]
    public void RealFixture_HasThreeInadvertentCvxCodes()
    {
        Assert.Equal(new[] { "178", "179", "182" }, PolioDose1InadvertentCvxCodes);
    }

    [Fact]
    public void MatchingCvx_IsNotValidWithInadvertentAdministrationReason()
    {
        var result = EvaluateInadvertentVaccine.Execute("178", PolioDose1InadvertentCvxCodes);

        Assert.False(result.IsValid);
        Assert.Equal("Inadvertent Administration", result.Reason);
    }

    [Fact]
    public void NonMatchingCvx_IsValid()
    {
        // CVX 10 = IPV (injectable Polio) - the normal, non-inadvertent Polio vaccine.
        var result = EvaluateInadvertentVaccine.Execute("10", PolioDose1InadvertentCvxCodes);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyInadvertentList_IsAlwaysValid()
    {
        var result = EvaluateInadvertentVaccine.Execute("178", Array.Empty<string>());

        Assert.True(result.IsValid);
    }
}
