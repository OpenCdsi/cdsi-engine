/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluateContraindicationsTests
{
    // Real data: Measles antigen-level contraindications include "007" (Pregnant, no age gate)
    // and "091" (Severe allergic reaction after previous dose of Measles).
    private static readonly AntigenContraindicationData MeaslesContraindications =
        AntigenSupportingDataLoader.LoadContraindicationData(TestPaths.AntigenFile("AntigenSupportingData-_Measles-508.xml"));

    // Real data: RSV has exactly one age-gated antigen-level contraindication - "278" (Birth
    // mother received RSV vaccine during pregnancy), beginAge "0 days", endAge "8 months".
    private static readonly AntigenContraindicationData RsvContraindications =
        AntigenSupportingDataLoader.LoadContraindicationData(TestPaths.AntigenFile("AntigenSupportingData-_RSV-508.xml"));

    // Real data: Measles vaccine-level contraindication "089" (Severe allergic reaction after
    // previous dose of Varicella) -> contraindicatedVaccine CVX "94" (MMRV, which includes the
    // Measles antigen alongside Mumps/Rubella/Varicella - hence this entry living in the
    // Measles file's own vaccine-level contraindications, not a Varicella-specific file).
    private static AntigenContraindicationData MeaslesVaccineLevelSource => MeaslesContraindications;

    private static Patient MakePatient(DateOnly dob, IReadOnlyList<PatientObservation>? observations = null,
        IReadOnlyList<PatientObservation>? adverseReactions = null, IReadOnlyList<string>? unresolvedCodes = null) => new()
    {
        PatientId = "p1",
        DateOfBirth = dob,
        ActiveObservations = observations ?? Array.Empty<PatientObservation>(),
        AdverseReactions = adverseReactions ?? Array.Empty<PatientObservation>(),
        UnresolvedObservationCodes = unresolvedCodes ?? Array.Empty<string>()
    };

    private static AntigenContraindication Pregnant =>
        MeaslesContraindications.AntigenLevel.Single(c => c.ObservationCode == "007");

    [Fact]
    public void MatchingActiveObservation_ContraindicationApplies()
    {
        var patient = MakePatient(new DateOnly(1990, 1, 1), observations: new[] { new PatientObservation { Code = "007" } });

        var result = EvaluateContraindications.EvaluateAntigenContraindication(patient, new DateOnly(2024, 1, 1), Pregnant);

        Assert.Equal(ContraindicationApplicability.Applies, result);
    }

    [Fact]
    public void MatchingAdverseReaction_NotActiveObservation_ContraindicationStillApplies()
    {
        // Proves the dual-bucket check: code "091" found only in AdverseReactions, not
        // ActiveObservations, still resolves to Applies.
        var reaction = MeaslesContraindications.AntigenLevel.Single(c => c.ObservationCode == "091");
        var patient = MakePatient(new DateOnly(1990, 1, 1), adverseReactions: new[] { new PatientObservation { Code = "091" } });

        var result = EvaluateContraindications.EvaluateAntigenContraindication(patient, new DateOnly(2024, 1, 1), reaction);

        Assert.Equal(ContraindicationApplicability.Applies, result);
    }

    [Fact]
    public void NoMatchingCodeAnywhere_DoesNotApply()
    {
        var patient = MakePatient(new DateOnly(1990, 1, 1));

        var result = EvaluateContraindications.EvaluateAntigenContraindication(patient, new DateOnly(2024, 1, 1), Pregnant);

        Assert.Equal(ContraindicationApplicability.DoesNotApply, result);
    }

    [Fact]
    public void UnresolvedObservationCode_IsUnresolved_NotAppliesOrDoesNotApply()
    {
        var patient = MakePatient(new DateOnly(1990, 1, 1), unresolvedCodes: new[] { "007" });

        var result = EvaluateContraindications.EvaluateAntigenContraindication(patient, new DateOnly(2024, 1, 1), Pregnant);

        Assert.Equal(ContraindicationApplicability.Unresolved, result);
    }

    [Fact]
    public void AgeGatedContraindication_WithinWindow_MatchingObservation_Applies()
    {
        var rule = RsvContraindications.AntigenLevel.Single(c => c.ObservationCode == "278");
        var dob = new DateOnly(2024, 1, 1);
        var patient = MakePatient(dob, observations: new[] { new PatientObservation { Code = "278" } });

        // Within [0 days, 8 months) of DOB.
        var result = EvaluateContraindications.EvaluateAntigenContraindication(patient, new DateOnly(2024, 6, 1), rule);

        Assert.Equal(ContraindicationApplicability.Applies, result);
    }

    [Fact]
    public void AgeGatedContraindication_OutsideWindow_DoesNotApply_EvenWithMatchingObservation()
    {
        var rule = RsvContraindications.AntigenLevel.Single(c => c.ObservationCode == "278");
        var dob = new DateOnly(2024, 1, 1);
        var patient = MakePatient(dob, observations: new[] { new PatientObservation { Code = "278" } });

        // Past the 8-month endAge - age dominates even though the observation matches.
        var result = EvaluateContraindications.EvaluateAntigenContraindication(patient, new DateOnly(2025, 1, 1), rule);

        Assert.Equal(ContraindicationApplicability.DoesNotApply, result);
    }

    [Fact]
    public void VaccineContraindication_MatchingVaccineType_AndObservation_Applies()
    {
        var contraindication = MeaslesVaccineLevelSource.VaccineLevel.Single(c => c.ObservationCode == "089");
        var patient = MakePatient(new DateOnly(2015, 1, 1), observations: new[] { new PatientObservation { Code = "089" } });

        // CVX "94" is the contraindicated vaccine (MMRV) for this rule.
        var result = EvaluateContraindications.EvaluateVaccineContraindication(patient, new DateOnly(2024, 1, 1), "94", contraindication);

        Assert.Equal(ContraindicationApplicability.Applies, result);
    }

    [Fact]
    public void VaccineContraindication_DifferentVaccineType_DoesNotApply_EvenWithMatchingObservation()
    {
        // CVX "21" (plain Varicella) is NOT one of the contraindicated types for this rule
        // (only MMRV/"94" is) - vaccine-type mismatch means it doesn't apply, regardless of
        // the observation matching.
        var contraindication = MeaslesVaccineLevelSource.VaccineLevel.Single(c => c.ObservationCode == "089");
        var patient = MakePatient(new DateOnly(2015, 1, 1), observations: new[] { new PatientObservation { Code = "089" } });

        var result = EvaluateContraindications.EvaluateVaccineContraindication(patient, new DateOnly(2024, 1, 1), "21", contraindication);

        Assert.Equal(ContraindicationApplicability.DoesNotApply, result);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void IsContraindicatedPatientSeries_CombinesAntigenAndVaccineLevelCorrectly(
        bool anyAntigenApplies, bool allVaccinesContraindicated, bool expectedContraindicated)
    {
        var result = EvaluateContraindications.IsContraindicatedPatientSeries(anyAntigenApplies, allVaccinesContraindicated);

        Assert.Equal(expectedContraindicated, result);
    }
}
