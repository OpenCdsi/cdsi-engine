/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

/// <summary>
/// The full end-to-end pipeline, real data, no mocks anywhere: raw administered doses in, a
/// merged vaccine group forecast out. Every layer this project has built - §4.2/§5.1, §4.4/§6,
/// §7, §8, §9 - runs for real in these tests, not just its own isolated unit tests.
///
/// SCOPING NOTE: real HepB has 10 Standard-type series in series group "1" alone. For a patient
/// with no active observations (every test patient here), all 10 become simultaneously
/// "relevant" (§5.1) and would genuinely compete in §8's scoring - which series "wins" depends
/// on evaluating all 10 against the dose history, something this environment has no way to
/// execute and verify (no dotnet runtime available here to actually run the code). Rather than
/// assert an exact EarliestDate/dose-number for an unverified 10-way competition, these tests
/// deliberately scope `allSeries` down to the single series already hand-verified in isolation
/// in GeneratePatientSeriesForecastTests - the pipeline still runs every real stage genuinely
/// end-to-end, just without a real multi-candidate §8 contest layered on top that couldn't be
/// checked here. Running the true, full 18-series HepB catalog through this pipeline (and
/// confirming which series actually wins) is real, valuable follow-up work once a runtime is
/// available to verify it against.
/// </summary>
public class GeneratePatientForecastTests
{
    private static readonly ScheduleSupportingData Schedule =
        ScheduleSupportingDataLoader.LoadFile(TestPaths.ScheduleFilePath);

    private static readonly IReadOnlyList<VaccineGroupInfo> VaccineGroups =
        ScheduleSupportingDataLoader.LoadVaccineGroups(TestPaths.ScheduleFilePath);

    private static readonly IReadOnlyList<AntigenSeries> HepB3DoseSeriesOnly =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"))
            .Where(s => s.SeriesName == "HepB 3-dose series")
            .ToArray();

    private static readonly IReadOnlyDictionary<string, AntigenImmunityData> ImmunityByAntigen =
        new Dictionary<string, AntigenImmunityData>
        {
            ["HepB"] = AntigenSupportingDataLoader.LoadImmunityData(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"))
        };

    private static readonly IReadOnlyDictionary<string, AntigenContraindicationData> ContraindicationsByAntigen =
        new Dictionary<string, AntigenContraindicationData>
        {
            ["HepB"] = AntigenSupportingDataLoader.LoadContraindicationData(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"))
        };

    private static Patient MakePatient(DateOnly dob) => new() { PatientId = "p1", DateOfBirth = dob };

    [Fact]
    public void RealPatient_TwoOfThreeHepBDosesGiven_ProducesNotCompleteVaccineGroupForecast()
    {
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 1) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "08", DateAdministered = new DateOnly(2020, 3, 1) }
        };

        var results = GeneratePatientForecast.Execute(
            patient, doses, HepB3DoseSeriesOnly, Schedule, VaccineGroups,
            ImmunityByAntigen, ContraindicationsByAntigen,
            assessmentDate: new DateOnly(2020, 9, 1));

        var hepB = Assert.Single(results);
        Assert.Equal("HepB", hepB.VaccineGroupName);
        Assert.Equal(VaccineGroupType.SingleAntigen, hepB.Type);
        Assert.Equal(PatientSeriesStatus.NotComplete, hepB.Status);
        Assert.True(hepB.ShouldForecast);
        Assert.Equal(3, hepB.ForecastDoseNumber);
        Assert.NotNull(hepB.EarliestDate);

        // Matches the hand-traced value from GeneratePatientSeriesForecastTests' equivalent
        // single-series scenario - the merge should pass this straight through unchanged since
        // there's only one contained forecast for this single-antigen group.
        Assert.Equal(new DateOnly(2020, 6, 17), hepB.EarliestDate);

        // Confirms AllPreferableVaccineCvxCodes survives the full pipeline, not just the
        // per-series layer: still empty for RecommendedVaccineCvxCodes (real HepB Dose 3 data,
        // no forecastVaccineType='Y' entries), but populated for the broader field.
        Assert.Empty(hepB.RecommendedVaccineCvxCodes);
        Assert.Contains("08", hepB.AllPreferableVaccineCvxCodes);
    }

    [Fact]
    public void RealPatient_AllThreeHepBDosesGiven_ProducesCompleteVaccineGroupForecast_DoesNotForecast()
    {
        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 1) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "08", DateAdministered = new DateOnly(2020, 3, 1) },
            new VaccineDoseAdministered { DoseId = "d3", Cvx = "08", DateAdministered = new DateOnly(2020, 9, 1) }
        };

        var results = GeneratePatientForecast.Execute(
            patient, doses, HepB3DoseSeriesOnly, Schedule, VaccineGroups,
            ImmunityByAntigen, ContraindicationsByAntigen,
            assessmentDate: new DateOnly(2021, 1, 1));

        var hepB = Assert.Single(results);
        Assert.Equal(PatientSeriesStatus.Complete, hepB.Status);
        Assert.False(hepB.ShouldForecast);
        Assert.Null(hepB.EarliestDate);
        Assert.Null(hepB.ForecastDoseNumber);
    }

    [Fact]
    public void RealPatient_NoDosesAtAll_StillProducesForecastForDoseOne()
    {
        var dob = new DateOnly(2024, 1, 1);
        var patient = MakePatient(dob);

        var results = GeneratePatientForecast.Execute(
            patient, Array.Empty<VaccineDoseAdministered>(), HepB3DoseSeriesOnly, Schedule, VaccineGroups,
            ImmunityByAntigen, ContraindicationsByAntigen,
            assessmentDate: new DateOnly(2024, 1, 1));

        var hepB = Assert.Single(results);
        Assert.Equal(PatientSeriesStatus.NotComplete, hepB.Status);
        Assert.True(hepB.ShouldForecast);
        Assert.Equal(1, hepB.ForecastDoseNumber);
        Assert.Equal(dob, hepB.EarliestDate); // Dose 1 is a birth dose - absMinAge/minAge are both "0 days"
    }
}
