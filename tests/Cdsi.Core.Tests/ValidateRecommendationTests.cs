/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class ValidateRecommendationTests
{
    // Real data reused from EvaluateConditionalSkipTests: "Hib start at 2 months 4-dose series"
    // Dose 2 has a Forecast-context conditionalSkip instance with beginAge "15 months" exactly
    // (no grace period, unlike the Evaluation-context sibling instance).
    private static IReadOnlyList<ConditionalSkipInstance> HibDose2 =>
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Hib-508.xml"))
            .Single(s => s.SeriesName == "Hib start at 2 months 4-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 2).ConditionalSkipInstances;

    private static readonly Func<string?, bool> NoCompletedSeriesExpected =
        _ => throw new InvalidOperationException("Test fixture shouldn't reach a Completed Series condition.");

    [Fact]
    public void ForecastEarliestDateBeforeSkipThreshold_RecommendationIsValid()
    {
        var dob = new DateOnly(2020, 1, 1);
        // Forecast context threshold is exactly "15 months" -> 2021-04-01. A forecast earliest
        // date before that shouldn't be skippable, so the recommendation stays valid.
        var forecastEarliestDate = new DateOnly(2021, 3, 1);

        var isValid = ValidateRecommendation.IsValid(
            dob, forecastEarliestDate, HibDose2, Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.True(isValid);
    }

    [Fact]
    public void ForecastEarliestDateAtOrPastSkipThreshold_RecommendationIsInvalid()
    {
        var dob = new DateOnly(2020, 1, 1);
        // By the time the forecast's own earliest date arrives, the target dose would already
        // be skippable under Forecast context - this forecast is stale and needs re-forecasting.
        var forecastEarliestDate = new DateOnly(2021, 4, 1);

        var isValid = ValidateRecommendation.IsValid(
            dob, forecastEarliestDate, HibDose2, Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.False(isValid);
    }

    [Fact]
    public void NoConditionalSkipInstances_AlwaysValid()
    {
        var dob = new DateOnly(2020, 1, 1);

        var isValid = ValidateRecommendation.IsValid(
            dob, new DateOnly(2025, 1, 1), Array.Empty<ConditionalSkipInstance>(),
            Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.True(isValid);
    }

    [Fact]
    public void DiagnosticOnly_RealPertussisDose8_ForecastContextSkip_AgainstOneExistingAdultDose()
    {
        // DIAGNOSTIC, not a fix - written specifically to confirm or deny a hypothesis about
        // real corpus cases 2020-0004/2020-0005 (adult DTaP/Tdap/Td catch-up patients) before
        // touching any production code, after getting a confident hand-trace wrong once already
        // this session (see EvaluateSeriesHistory's own class doc comment for that story).
        //
        // Real Dose 8 of the Pertussis standard series has its own standalone Forecast-context
        // Conditional Skip condition: "Vaccine Count by Age, beginAge 7 years, doseCount 0,
        // doseType Valid, doseCountLogic greater than" - i.e. "skip if the patient already has
        // more than 0 valid doses counted from age 7 onward." The real corpus patient (DOB
        // 1995-08-05) has exactly one valid dose, CVX115/Tdap, given 2026-08-05 at age 31 - which
        // satisfies "a valid dose at age >= 7" on its face. If this test finds the recommendation
        // INVALID, that confirms the §7.6 re-forecast loop (built for the Hib catch-up scenario
        // in an earlier round) would retry past Dose 8 for this patient too - a real, distinct
        // mechanism from the reverted Dose 7 auto-satisfy assumption, and the leading candidate
        // for why 2020-0004/2020-0005 still don't match the corpus.
        var pertussisDose8 = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Pertussis-508.xml"))
            .Single(s => s.SeriesName == "Pertussis standard series")
            .SeriesDoses.Single(d => d.DoseNumber == 8).ConditionalSkipInstances;

        var dob = new DateOnly(1995, 8, 5);
        var forecastEarliestDate = new DateOnly(2026, 9, 2); // Dose 8's own real candidateEarliestDate: Dose1Date (2026-08-05) + its real 4-week interval
        var priorDoses = new[]
        {
            new PriorVaccineDoseAdministered("115", new DateOnly(2026, 8, 5), PriorDoseEvaluationStatus.Valid)
        };

        var isValid = ValidateRecommendation.IsValid(
            dob, forecastEarliestDate, pertussisDose8, priorDoses, NoCompletedSeriesExpected);

        // If this fails (isValid comes back true), the hypothesis above is wrong and the real
        // explanation lies elsewhere - genuinely useful either way, which is the point of writing
        // this as a real, checkable test rather than continuing to hand-trace unverified.
        Assert.False(isValid);
    }

    [Fact]
    public void DiagnosticOnly_RealPertussisDose8_ForecastContextSkip_WithoutThatSameDoseCounted()
    {
        // DIAGNOSTIC, not a fix - companion to the diagnostic immediately above. That test
        // confirmed real corpus case 2020-0004's re-forecast loop finds Dose 8 "invalid" because
        // its own Forecast-context skip condition ("doseCount > 0 valid doses at age 7+") is
        // satisfied by this patient's one existing dose - the SAME dose that just satisfied
        // Dose 7, the target dose immediately prior in the evaluation chain that got the patient
        // to Dose 8 in the first place.
        //
        // The real corpus's own metadata (meta.forecastTestType: "Recommended based on minimum
        // interval from previous dose (catch-up)") directly confirms Dose 8's own forecast IS
        // the intended answer here - meaning this skip shouldn't be firing at all for this
        // patient. Testing a specific hypothesis for why: unlike the Hib worked example in
        // §7.6's own spec text (a genuinely time-sensitive AGE condition, where the patient's
        // age legitimately changes by the time they return), THIS is a dose-count condition -
        // already true the instant the qualifying dose was given, not something that becomes
        // newly true over time the way §7.6 was designed around. If the dose that JUST satisfied
        // the immediately-prior target dose isn't meant to also count toward a LATER dose's own
        // "do you already have one of these" check, removing it here should flip this specific
        // skip from satisfied to not-satisfied.
        var pertussisDose8 = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Pertussis-508.xml"))
            .Single(s => s.SeriesName == "Pertussis standard series")
            .SeriesDoses.Single(d => d.DoseNumber == 8).ConditionalSkipInstances;

        var dob = new DateOnly(1995, 8, 5);
        var forecastEarliestDate = new DateOnly(2026, 9, 2);

        // The only difference from the diagnostic above: the one existing dose is NOT included
        // here, simulating "exclude whatever dose satisfied the immediately-prior target dose."
        var isValid = ValidateRecommendation.IsValid(
            dob, forecastEarliestDate, pertussisDose8, Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        // If this comes back true (valid), that confirms the hypothesis: excluding the dose that
        // satisfied Dose 7 from Dose 8's own doseCount check would make Dose 8 correctly NOT
        // skippable, matching the corpus's own expected answer. If it comes back false anyway,
        // something else about this skip condition is triggering independent of that one dose,
        // and the hypothesis needs rethinking.
        Assert.True(isValid);
    }
}
