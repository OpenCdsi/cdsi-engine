/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using System.Text;
using System.Text.Json;
using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace OpenCdsi.VaxEngine.Conformance.Tests;

/// <summary>
/// Runs the real engine against a real, external 1,064-case conformance corpus
/// (cdsi-healthy-test-cases.json - see CorpusModels.cs for the open provenance/licensing
/// question this corpus's own file still carries).
///
/// Two decisions made before writing this file, both explicitly confirmed rather than assumed:
/// 1. Per-dose Valid/Not Valid status is asserted strictly; the corpus's CDC-category reason
///    text ("Age: Too Young") is never asserted against the engine's own terser reason strings
///    ("Too young") - they were never going to string-match, and building a translation table
///    for ~1,064 cases' worth of reason text was judged not worth doing versus just reporting
///    mismatches for visibility. Reason mismatches are written to test output, not failed on.
/// 2. Each corpus case collects ALL of its own mismatches (series status, forecast dates, every
///    dose's status) before asserting once, rather than stopping at the first Assert failure -
///    on a first real run against 1,064 cases, seeing every issue within a case at once is far
///    more useful than rerunning 1,064 times to find them one at a time.
/// </summary>
public class ConformanceTests : IClassFixture<ReferenceDataFixture>
{
    private readonly ReferenceDataFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ConformanceTests(ReferenceDataFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public static IEnumerable<object[]> GetCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "cdsi-healthy-test-cases.json");
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var cases = JsonSerializer.Deserialize<List<ConformanceTestCase>>(json, options)
            ?? throw new InvalidOperationException("Corpus deserialized to null - check cdsi-healthy-test-cases.json is present under TestData/.");
        return cases.Select(c => new object[] { c });
    }

    [Theory]
    [MemberData(nameof(GetCases))]
    public void ConformsToExpectedForecast(ConformanceTestCase testCase)
    {
        var repo = _fixture.Repository;
        var mismatches = new List<string>();

        if (!VaccineGroupMapping.CorpusToEngineVaccineGroup.TryGetValue(testCase.VaccineGroup, out var engineGroupName))
        {
            Assert.Fail($"[{testCase}] Unmapped corpus vaccineGroup code: '{testCase.VaccineGroup}' - VaccineGroupMapping needs a new entry.");
            return;
        }

        var patient = new Patient
        {
            PatientId = testCase.TestId,
            DateOfBirth = testCase.Patient.Dob,
            Gender = VaccineGroupMapping.ToGender(testCase.Patient.Gender)
        };

        var doses = testCase.ImmunizationHistory
            .Select((d, i) => new VaccineDoseAdministered
            {
                DoseId = $"{testCase.TestId}-dose-{i}",
                Cvx = d.Cvx,
                DateAdministered = d.DateAdministered
            })
            .ToArray();

        var result = GeneratePatientForecast.ExecuteWithDoseDetail(
            patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
            repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, testCase.AssessmentDate);

        // §9-level: find the matching vaccine group forecast. Trimmed on both sides - the real
        // engine's own VaccineGroupName can carry a trailing space (e.g. "Zoster ") that the
        // ElementTextOrNull loader deliberately doesn't strip (confirmed against its actual
        // source before writing this comparison) - not something either side should have to
        // match exactly to be considered the same group.
        var groupResult = result.VaccineGroupForecasts
            .SingleOrDefault(r => r.VaccineGroupName.Trim() == engineGroupName);

        if (groupResult is null)
        {
            Assert.Fail($"[{testCase}] No vaccine group forecast found for '{engineGroupName}' (mapped from corpus code '{testCase.VaccineGroup}'). " +
                        $"Groups actually returned: {string.Join(", ", result.VaccineGroupForecasts.Select(r => $"'{r.VaccineGroupName}'"))}");
            return;
        }

        var expectedSeriesStatus = VaccineGroupMapping.CorpusToEngineSeriesStatus[testCase.SeriesStatus];
        if (expectedSeriesStatus != groupResult.Status)
        {
            mismatches.Add($"seriesStatus: expected {expectedSeriesStatus} ('{testCase.SeriesStatus}'), got {groupResult.Status}");
        }

        if (testCase.Forecast.EarliestDate is DateOnly expectedEarliest && expectedEarliest != groupResult.EarliestDate)
        {
            mismatches.Add($"forecast.earliestDate: expected {expectedEarliest:yyyy-MM-dd}, got {groupResult.EarliestDate:yyyy-MM-dd}");
        }
        if (testCase.Forecast.RecommendedDate is DateOnly expectedRecommended && expectedRecommended != groupResult.AdjustedRecommendedDate)
        {
            mismatches.Add($"forecast.recommendedDate: expected {expectedRecommended:yyyy-MM-dd}, got {groupResult.AdjustedRecommendedDate:yyyy-MM-dd}");
        }
        if (testCase.Forecast.PastDueDate is DateOnly expectedPastDue && expectedPastDue != groupResult.AdjustedPastDueDate)
        {
            mismatches.Add($"forecast.pastDueDate: expected {expectedPastDue:yyyy-MM-dd}, got {groupResult.AdjustedPastDueDate:yyyy-MM-dd}");
        }
        if (testCase.Forecast.ForecastNumber is string expectedNumberText)
        {
            var expectedNumber = int.Parse(expectedNumberText);
            if (expectedNumber != groupResult.ForecastDoseNumber)
            {
                mismatches.Add($"forecast.forecastNumber: expected {expectedNumber}, got {groupResult.ForecastDoseNumber}");
            }
        }

        // §6-level: per-dose Valid/Not Valid, matched by (Cvx, DateAdministered) - not by the
        // corpus's own doseNumber, which is the chronological administration sequence, not the
        // engine's internal TargetDoseNumber (see ConformanceDose's own doc comment for why
        // those two numbers aren't the same thing).
        //
        // Searches ACROSS EVERY antigen the engine tracked for this patient, not just the
        // antigens belonging to this test case's own vaccine group - a real, corrected design
        // decision, not the original one. Real corpus cases legitimately mix doses from a
        // DIFFERENT vaccine group into immunizationHistory specifically to test cross-vaccine
        // interactions (e.g. an "MMR" test case's history including a Varicella dose, to test
        // whether a too-soon MMR-after-Varicella interval makes the MMR dose invalid - confirmed
        // by reading actual failing cases from this corpus's first real run, not assumed). A
        // search scoped to only this case's own vaccine group's antigens could never find that
        // dose at all.
        foreach (var dose in testCase.ImmunizationHistory)
        {
            var expectedDoseStatus = VaccineGroupMapping.CorpusToEngineEvaluationStatus[dose.ExpectedStatus];

            var candidateRecords = result.DoseDetailsByAntigen.Values
                .SelectMany(h => h.DoseResults)
                .Where(r => r.AdministeredDose.Cvx == dose.Cvx && r.AdministeredDose.DateAdministered == dose.DateAdministered)
                .ToArray();

            // The SAME administered dose can appear against more than one target dose in
            // sequence: the two-pointer algorithm advances the target-dose pointer WITHOUT
            // consuming the administered dose on a Skip (confirmed real and common for
            // DTaP/Tdap/Td specifically - Pertussis alone has dozens of real per-dose Age and
            // "Vaccine Count by Age" conditional-skip conditions governing the under-7/7-plus
            // transition). A Skipped target dose's EvaluationStatus is null by design (it was
            // never actually evaluated for Valid/NotValid - see TargetDoseEvaluationResult.Skipped's
            // own doc comment) - a naive "take the first match" grabs that uninteresting Skipped
            // record instead of the meaningful Satisfied/NotSatisfied one that comes later for
            // the same administered dose. Prefer a non-Skipped record when one exists; this was
            // a real bug found on this corpus's first real run (104 "expected Valid, got null"
            // failures, concentrated almost entirely in DTaP/Tdap/Td cases), not a hypothetical.
            var matchedRecord = candidateRecords.FirstOrDefault(r => r.Result.TargetDoseStatus != TargetDoseStatus.Skipped)
                ?? candidateRecords.FirstOrDefault();

            if (matchedRecord is null)
            {
                mismatches.Add($"dose CVX {dose.Cvx} on {dose.DateAdministered:yyyy-MM-dd}: no matching evaluation result found across any of the {result.DoseDetailsByAntigen.Count} antigen(s) tracked for this patient [{string.Join(", ", result.DoseDetailsByAntigen.Keys)}]");
                continue;
            }

            if (expectedDoseStatus != matchedRecord.Result.EvaluationStatus)
            {
                mismatches.Add($"dose CVX {dose.Cvx} on {dose.DateAdministered:yyyy-MM-dd}: expected {expectedDoseStatus} ('{dose.ExpectedStatus}'), got {matchedRecord.Result.EvaluationStatus?.ToString() ?? "null"} (TargetDoseStatus={matchedRecord.Result.TargetDoseStatus})");
            }

            if (dose.ExpectedReason is not null && dose.ExpectedReason != matchedRecord.Result.Reason)
            {
                // Logged, not a failure - see class doc comment. Genuinely useful signal on
                // whether the two vocabularies are at least pointing at the same underlying
                // cause, without pretending they'll ever string-match.
                _output.WriteLine($"[{testCase}] reason text differs (not asserted): corpus='{dose.ExpectedReason}' engine='{matchedRecord.Result.Reason ?? "null"}'");
            }
        }

        if (mismatches.Count > 0)
        {
            var detail = new StringBuilder();
            detail.AppendLine($"[{testCase}] {mismatches.Count} mismatch(es):");
            foreach (var m in mismatches)
            {
                detail.AppendLine($"  - {m}");
            }
            Assert.Fail(detail.ToString());
        }
    }
}
