/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Xunit;

namespace Cdsi.Conformance.Tests;

/// <summary>
/// Investigating (and, for 2024-0032, confirming a real fix for) all 18 real MenB conformance
/// failures at once. Every one of them fails identically, before any forecast computation even
/// happens: "No vaccine group forecast found for 'Meningococcal B'... Groups actually returned:
/// ... 'Meningococcal', ..." (missing 'Meningococcal B' specifically, even though
/// 'Meningococcal' - a real, different group, MenACWY - is present).
///
/// The real source data genuinely defines "Meningococcal B" as its own, separate vaccine group -
/// not a naming mismatch to fix in code. Two further hypotheses checked and disproven (a stale
/// local build; the real "Shared Clinical Decision Making" series names being structurally
/// excluded somehow) before tracing the real pipeline directly for 2024-0032 (DOB 2006-08-05,
/// zero doses, 20 years old at assessment - see DiagnosticOnly_2024_0032_..._WhereDoesMening-
/// ococcalBDropOut, kept below) found the real cause: 4 relevant, equally-scored "Shared
/// Clinical Decision Making" series, none of which has a seriesPreference at all (confirmed real
/// data - genuinely absent, not a parsing gap). SelectPrioritizedPatientSeries.Execute's own
/// previous tie-break logic returned null whenever no tied top-scorer had ANY seriesPreference
/// to compare - which cascaded silently all the way up, and the entire Meningococcal B vaccine
/// group vanished from the final output with no error at all.
///
/// Confirmed as a genuine bug (not spec ambiguity) by §8.8's own text: "This step only happens
/// after ONE prioritized patient series has been selected for each Series Group" - fixed in
/// SelectPrioritizedPatientSeries.cs itself (see its own doc comment for the full derivation) by
/// falling back to a deterministic choice (by series name) whenever a tie survives both the
/// score comparison and the seriesPreference comparison, rather than giving up.
/// </summary>
public class MeningococcalBInvestigationTests : IClassFixture<ReferenceDataFixture>
{
    private readonly ReferenceDataFixture _fixture;

    public MeningococcalBInvestigationTests(ReferenceDataFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Real_2024_0032_ZeroDose20YearOld_MeningococcalBNowCorrectlyForecast()
    {
        // Real verification, not a diagnostic - confirms the SelectPrioritizedPatientSeries fix
        // directly against this exact real corpus case before trusting it against the full
        // 1,064-case corpus. The corpus's own expected forecast for this case (a genuinely
        // zero-dose 20-year-old): forecastNumber 1, earliestDate/recommendedDate both
        // 2022-08-05, no pastDueDate.
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2024-0032-verify", DateOfBirth = new DateOnly(2006, 8, 5) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = Array.Empty<VaccineDoseAdministered>();

        var fullResult = GeneratePatientForecast.ExecuteWithDoseDetail(
            patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
            repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, assessmentDate);
        var menBGroup = fullResult.VaccineGroupForecasts.SingleOrDefault(g => g.VaccineGroupName.Trim() == "Meningococcal B");

        Assert.NotNull(menBGroup);
        Assert.Equal(new DateOnly(2022, 8, 5), menBGroup!.EarliestDate);
        Assert.Equal(new DateOnly(2022, 8, 5), menBGroup.AdjustedRecommendedDate);
    }

    [Fact]
    public void DiagnosticOnly_IsMeningococcalBAntigenDataActuallyLoaded()
    {
        var repo = _fixture.Repository;

        var distinctAntigens = repo.AllSeries.Select(s => s.Antigen).Distinct().OrderBy(a => a, StringComparer.Ordinal).ToArray();
        var meningococcalBSeriesCount = repo.AllSeries.Count(s => s.Antigen == "Meningococcal B");
        var meningococcalBInVaccineGroups = repo.VaccineGroups.Any(g => g.Name == "Meningococcal B");

        Assert.True(false,
            $"MeningococcalB series count={meningococcalBSeriesCount} | " +
            $"'Meningococcal B' key present in repo.VaccineGroups={meningococcalBInVaccineGroups} | " +
            $"All distinct antigens loaded ({distinctAntigens.Length}): {string.Join(", ", distinctAntigens)}");
    }

    [Fact]
    public void DiagnosticOnly_2024_0032_ZeroDose20YearOld_WhereDoesMeningococcalBDropOut()
    {
        // DIAGNOSTIC, not a fix. The first diagnostic disproved the stale-build hypothesis
        // cleanly: real execution confirmed Meningococcal B antigen data (6 series) genuinely
        // loads correctly via this same ReferenceDataFixture. Also checked, by direct data
        // inspection rather than guessing: whether Meningococcal B's own "Shared Clinical
        // Decision Making" series name might mean it's structurally excluded somehow - but its
        // real seriesType field says "Standard" and its indication field is "None", so nothing
        // in the structured data marks it as special; that hypothesis is disproven too, before
        // even building a test for it.
        //
        // Tracing the real pipeline directly for 2024-0032 (DOB 2006-08-05, zero doses, 20 years
        // old at assessment) to find exactly where "Meningococcal B" drops out between
        // CreateRelevantPatientSeries (which should find it relevant) and the final
        // VaccineGroupForecasts list (which the real conformance failure shows doesn't include
        // it at all).
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2024-0032", DateOfBirth = new DateOnly(2006, 8, 5) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = Array.Empty<VaccineDoseAdministered>();

        var relevantResult = CreateRelevantPatientSeries.Execute(patient, repo.AllSeries, assessmentDate);
        var relevantMenBSeries = relevantResult.RelevantSeries.Where(s => s.Antigen == "Meningococcal B").ToArray();
        var unresolvedMenB = relevantResult.UnresolvedIndications.Where(u => u.Antigen == "Meningococcal B").ToArray();

        var fullResult = GeneratePatientForecast.ExecuteWithDoseDetail(
            patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
            repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, assessmentDate);
        var groupNames = string.Join(", ", fullResult.VaccineGroupForecasts.Select(g => $"'{g.VaccineGroupName}'"));
        var menBDetail = fullResult.DoseDetailsByAntigen.GetValueOrDefault("Meningococcal B");

        Assert.True(false,
            $"Relevant MenB series count={relevantMenBSeries.Length} ({string.Join(", ", relevantMenBSeries.Select(s => s.SeriesName))}) | " +
            $"Unresolved MenB indications={unresolvedMenB.Length} ({string.Join(", ", unresolvedMenB.Select(u => u.SeriesName + ":" + u.Description))}) | " +
            $"MenB dose detail present={menBDetail is not null} CurrentTargetDoseNumber={menBDetail?.CurrentTargetDoseNumber} | " +
            $"Final VaccineGroupForecasts ({fullResult.VaccineGroupForecasts.Count}): {groupNames}");
    }
}
