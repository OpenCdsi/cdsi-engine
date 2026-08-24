/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Xunit;

namespace Cdsi.Core.Tests;

public class GenerateForecastDatesTests
{
    [Fact]
    public void CandidateEarliestDate_TakesMaxOfNonNullComponents()
    {
        var result = GenerateForecastDates.CalculateCandidateEarliestDate(
            minAgeDate: new DateOnly(2023, 1, 1),
            latestMinIntervalDate: new DateOnly(2023, 6, 1),
            latestConflictEndDate: null,
            seasonalRecommendationStartDate: new DateOnly(1900, 1, 1),
            latestInadvertentAdministrationDate: null,
            mostRecentAdministeredDate: new DateOnly(2023, 3, 1));

        Assert.Equal(new DateOnly(2023, 6, 1), result);
    }

    [Fact]
    public void CandidateEarliestDate_AllNullExceptSeasonalDefault_ReturnsSeasonalDefault()
    {
        var result = GenerateForecastDates.CalculateCandidateEarliestDate(
            minAgeDate: null, latestMinIntervalDate: null, latestConflictEndDate: null,
            seasonalRecommendationStartDate: new DateOnly(1900, 1, 1),
            latestInadvertentAdministrationDate: null, mostRecentAdministeredDate: null);

        Assert.Equal(new DateOnly(1900, 1, 1), result);
    }

    [Fact]
    public void CandidateEarliestDate_SeasonalStartDateCanWin_WhenLaterThanEverythingElse()
    {
        var result = GenerateForecastDates.CalculateCandidateEarliestDate(
            minAgeDate: new DateOnly(2023, 1, 1), latestMinIntervalDate: null, latestConflictEndDate: null,
            seasonalRecommendationStartDate: new DateOnly(2025, 9, 1),
            latestInadvertentAdministrationDate: null, mostRecentAdministeredDate: null);

        Assert.Equal(new DateOnly(2025, 9, 1), result);
    }

    [Fact]
    public void RecommendedDate_FallsBackToInterval_WhenNoEarliestRecAgeDate()
    {
        var result = GenerateForecastDates.Execute(
            candidateEarliestDate: new DateOnly(2023, 1, 1),
            earliestRecAgeDate: null,
            latestEarliestRecIntervalDate: new DateOnly(2023, 4, 1),
            latestRecAgeDate: null,
            latestLatestRecIntervalDate: null,
            maxAgeDate: null);

        Assert.Equal(new DateOnly(2023, 4, 1), result.UnadjustedRecommendedDate);
    }

    [Fact]
    public void RecommendedDate_FallsBackToEarliestDate_WhenNeitherAgeNorIntervalAvailable()
    {
        var result = GenerateForecastDates.Execute(
            candidateEarliestDate: new DateOnly(2023, 1, 1),
            earliestRecAgeDate: null, latestEarliestRecIntervalDate: null,
            latestRecAgeDate: null, latestLatestRecIntervalDate: null, maxAgeDate: null);

        Assert.Equal(new DateOnly(2023, 1, 1), result.UnadjustedRecommendedDate);
    }

    [Fact]
    public void PastDueDate_IsBlank_WhenNoLatestRecAgeOrIntervalDate()
    {
        var result = GenerateForecastDates.Execute(
            candidateEarliestDate: new DateOnly(2023, 1, 1),
            earliestRecAgeDate: null, latestEarliestRecIntervalDate: null,
            latestRecAgeDate: null, latestLatestRecIntervalDate: null, maxAgeDate: null);

        Assert.Null(result.UnadjustedPastDueDate);
        Assert.Null(result.AdjustedPastDueDate);
    }

    [Fact]
    public void LatestDate_IsBlank_WhenNoMaxAgeDate()
    {
        var result = GenerateForecastDates.Execute(
            candidateEarliestDate: new DateOnly(2023, 1, 1),
            earliestRecAgeDate: null, latestEarliestRecIntervalDate: null,
            latestRecAgeDate: null, latestLatestRecIntervalDate: null, maxAgeDate: null);

        Assert.Null(result.LatestDate);
    }

    [Fact]
    public void AdjustedRecommendedDate_NeverBeforeEarliestDate()
    {
        // unadjustedRecommendedDate resolves to something BEFORE earliestDate (an edge case where
        // the recommended-age window predates the candidate earliest date, e.g. a late-start
        // catch-up scenario) - the adjusted date must not go backwards past earliestDate.
        var result = GenerateForecastDates.Execute(
            candidateEarliestDate: new DateOnly(2024, 6, 1),
            earliestRecAgeDate: new DateOnly(2023, 1, 1), // before candidateEarliestDate
            latestEarliestRecIntervalDate: null, latestRecAgeDate: null, latestLatestRecIntervalDate: null, maxAgeDate: null);

        Assert.Equal(new DateOnly(2023, 1, 1), result.UnadjustedRecommendedDate);
        Assert.Equal(new DateOnly(2024, 6, 1), result.AdjustedRecommendedDate); // clamped up to earliestDate
    }

    [Fact]
    public void FullRealDataEndToEnd_HpvDose1AgeFields()
    {
        // Real HPV 2-dose series Dose 1 data (used throughout this project's Age tests):
        // absMinAge "9 years - 4 days", minAge "9 years", earliestRecAge "11 years",
        // latestRecAge "13 years + 4 weeks", maxAge "46 years". DOB 2014-01-01.
        var dob = new DateOnly(2014, 1, 1);
        var minAgeDate = dob.AddYears(9);                       // 2023-01-01
        var earliestRecAgeDate = dob.AddYears(11);               // 2025-01-01
        var latestRecAgeDate = dob.AddYears(13).AddDays(28);     // 2027-01-29
        var maxAgeDate = dob.AddYears(46);                       // 2060-01-01

        var candidateEarliestDate = GenerateForecastDates.CalculateCandidateEarliestDate(
            minAgeDate, latestMinIntervalDate: null, latestConflictEndDate: null,
            seasonalRecommendationStartDate: new DateOnly(1900, 1, 1),
            latestInadvertentAdministrationDate: null, mostRecentAdministeredDate: null);

        var result = GenerateForecastDates.Execute(
            candidateEarliestDate, earliestRecAgeDate, latestEarliestRecIntervalDate: null,
            latestRecAgeDate, latestLatestRecIntervalDate: null, maxAgeDate);

        Assert.Equal(new DateOnly(2023, 1, 1), result.EarliestDate);
        Assert.Equal(new DateOnly(2025, 1, 1), result.UnadjustedRecommendedDate);
        Assert.Equal(new DateOnly(2027, 1, 28), result.UnadjustedPastDueDate);
        Assert.Equal(new DateOnly(2059, 12, 31), result.LatestDate);
        Assert.Equal(new DateOnly(2025, 1, 1), result.AdjustedRecommendedDate);
        Assert.Equal(new DateOnly(2027, 1, 28), result.AdjustedPastDueDate);
    }
}
