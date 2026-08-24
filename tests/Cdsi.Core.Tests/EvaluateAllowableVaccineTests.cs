/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluateAllowableVaccineTests
{
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    // Real data: "HepB 3-dose series" Dose 1 includes CVX 08 (Hep B, Adol/peds),
    // beginAge "0 days", endAge "20 years", among several allowable vaccine entries.
    private static IReadOnlyList<AllowableVaccine> HepB3DoseDose1 =>
        HepBSeries.Single(s => s.SeriesName == "HepB 3-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 1).AllowableVaccines;

    [Fact]
    public void MatchingCvxWithinAgeWindow_IsValid()
    {
        var result = EvaluateAllowableVaccine.Execute(
            new DateOnly(2020, 1, 1), "08", new DateOnly(2025, 1, 1), HepB3DoseDose1); // age 5

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NonMatchingCvx_IsNotValid()
    {
        var result = EvaluateAllowableVaccine.Execute(
            new DateOnly(2020, 1, 1), "999-not-a-real-cvx", new DateOnly(2025, 1, 1), HepB3DoseDose1);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void MatchingCvxOutsideAgeWindow_IsNotValidWithAgeReason()
    {
        // endAge "20 years" - administered at age 21 is past the window.
        var dob = new DateOnly(2000, 1, 1);
        var result = EvaluateAllowableVaccine.Execute(
            dob, "08", new DateOnly(2021, 6, 1), HepB3DoseDose1);

        Assert.False(result.IsValid);
        Assert.Contains("age range", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoAllowableVaccinesDefined_IsAlwaysValid()
    {
        var result = EvaluateAllowableVaccine.Execute(
            new DateOnly(2020, 1, 1), "999", new DateOnly(2025, 1, 1), Array.Empty<AllowableVaccine>());

        Assert.True(result.IsValid);
    }
}
