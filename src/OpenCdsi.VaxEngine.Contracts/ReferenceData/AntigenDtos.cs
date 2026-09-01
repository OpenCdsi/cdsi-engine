/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Contracts.ReferenceData;

/// <summary>
/// The reference-data API's own DTOs, mirroring OpenCdsi.VaxEngine.Core.ReferenceData's real model shapes
/// closely rather than a simplified/curated view - this is a "CDSi Supporting Data API," meant
/// to browse the real underlying reference data (including its deeply-nested rule structures),
/// not a forecast-facing summary the way ForecastResponseDto is.
///
/// DurationExpression fields (ages, intervals) are represented as their own ToString() output
/// (e.g. "6 months - 4 days") rather than decomposed into structured sub-fields - this is
/// literally the original CDC XML text these values were parsed from, so it's both the most
/// faithful representation and immediately recognizable to anyone cross-referencing the raw
/// supporting-data files. Enums are represented as their string names, matching this project's
/// own established convention (see ForecastResponseDto's own reasoning) - readable in a debugger
/// or a browser's network tab without a lookup table.
/// </summary>
public sealed class AntigenSummaryDto
{
    public required string Name { get; init; }
    public required int SeriesCount { get; init; }
}

public sealed class AntigenSeriesDto
{
    public required string SeriesName { get; init; }
    public required string Antigen { get; init; }
    public string? TargetDisease { get; init; }
    public string? VaccineGroup { get; init; }
    public required string SeriesType { get; init; }
    public required IReadOnlyList<string> RequiredGenders { get; init; }
    public required IReadOnlyList<IndicationDto> Indications { get; init; }
    public required IReadOnlyList<SeriesDoseDto> SeriesDoses { get; init; }
    public required IReadOnlyList<string> SeriesAdminGuidance { get; init; }
    public required SeriesGroupInfoDto SeriesGroupInfo { get; init; }
    public string? EquivalentSeriesGroup { get; init; }
}

public sealed class IndicationDto
{
    public string? ObservationCode { get; init; }
    public string? Description { get; init; }
    public string? BeginAge { get; init; }
    public string? EndAge { get; init; }
    public string? Guidance { get; init; }
}

public sealed class SeriesGroupInfoDto
{
    public required bool IsDefaultSeries { get; init; }
    public required bool IsProductPath { get; init; }
    public required string SeriesGroupName { get; init; }
    public required string SeriesGroup { get; init; }
    public required string SeriesPriority { get; init; }
    public int? SeriesPreference { get; init; }
    public string? MinAgeToStart { get; init; }
    public string? MaxAgeToStart { get; init; }
}

public sealed class SeriesDoseDto
{
    public required int DoseNumber { get; init; }
    public required IReadOnlyList<AgeRuleDto> AgeRules { get; init; }
    public required IReadOnlyList<PreferableIntervalRuleDto> PreferableIntervals { get; init; }
    public required IReadOnlyList<AllowableIntervalRuleDto> AllowableIntervals { get; init; }
    public required IReadOnlyList<string> InadvertentVaccineCvxCodes { get; init; }
    public required IReadOnlyList<PreferableVaccineDto> PreferableVaccines { get; init; }
    public required IReadOnlyList<AllowableVaccineDto> AllowableVaccines { get; init; }
    public required IReadOnlyList<ConditionalSkipInstanceDto> ConditionalSkipInstances { get; init; }
    public SeasonalRecommendationDto? SeasonalRecommendation { get; init; }
    public required bool IsRecurringDose { get; init; }
}

public sealed class SeasonalRecommendationDto
{
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
}

public sealed class PreferableVaccineDto
{
    public required string Cvx { get; init; }
    public string? BeginAge { get; init; }
    public string? EndAge { get; init; }
    public string? TradeName { get; init; }
    public double? Volume { get; init; }
    public required bool ForecastVaccineTypeFlag { get; init; }
}

public sealed class AllowableVaccineDto
{
    public required string Cvx { get; init; }
    public string? BeginAge { get; init; }
    public string? EndAge { get; init; }
}

public sealed class AgeRuleDto
{
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? CessationDate { get; init; }
    public string? AbsMinAge { get; init; }
    public string? MinAge { get; init; }
    public string? MaxAge { get; init; }
    public string? EarliestRecAge { get; init; }
    public string? LatestRecAge { get; init; }
}

public sealed class PreferableIntervalRuleDto
{
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? CessationDate { get; init; }
    public required string ReferenceType { get; init; }
    public int? ReferenceTargetDoseNumber { get; init; }
    public required IReadOnlyList<string> ReferenceVaccineCvxCodes { get; init; }
    public string? ReferenceObservationCode { get; init; }
    public string? AbsMinInt { get; init; }
    public string? MinInt { get; init; }
    public string? EarliestRecInt { get; init; }
    public string? LatestRecInt { get; init; }
    public string? IntervalPriority { get; init; }
}

public sealed class AllowableIntervalRuleDto
{
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? CessationDate { get; init; }
    public required string ReferenceType { get; init; }
    public int? ReferenceTargetDoseNumber { get; init; }
    public string? AbsMinInt { get; init; }
}

public sealed class ConditionalSkipInstanceDto
{
    public string? Context { get; init; }
    public string? SetLogic { get; init; }
    public required IReadOnlyList<ConditionalSkipSetDto> Sets { get; init; }
}

public sealed class ConditionalSkipSetDto
{
    public string? SetId { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? CessationDate { get; init; }
    public string? ConditionLogic { get; init; }
    public required IReadOnlyList<ConditionalSkipConditionDto> Conditions { get; init; }
}

public sealed class ConditionalSkipConditionDto
{
    public required string ConditionType { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? BeginAge { get; init; }
    public string? EndAge { get; init; }
    public int? DoseCount { get; init; }
    public string? DoseType { get; init; }
    public string? DoseCountLogic { get; init; }
    public required IReadOnlyList<string> VaccineTypeCvxCodes { get; init; }
    public string? Interval { get; init; }
    public string? SeriesGroups { get; init; }
}

public sealed class AntigenContraindicationsDto
{
    public required IReadOnlyList<AntigenContraindicationDto> AntigenLevel { get; init; }
    public required IReadOnlyList<VaccineContraindicationDto> VaccineLevel { get; init; }
}

public sealed class AntigenContraindicationDto
{
    public required string ObservationCode { get; init; }
    public string? ObservationTitle { get; init; }
    public string? ContraindicationText { get; init; }
    public string? ContraindicationGuidance { get; init; }
    public string? BeginAge { get; init; }
    public string? EndAge { get; init; }
}

public sealed class VaccineContraindicationDto
{
    public required string ObservationCode { get; init; }
    public string? ObservationTitle { get; init; }
    public string? ContraindicationText { get; init; }
    public string? ContraindicationGuidance { get; init; }
    public required IReadOnlyList<ContraindicatedVaccineDto> ContraindicatedVaccines { get; init; }
}

public sealed class ContraindicatedVaccineDto
{
    public required string Cvx { get; init; }
    public string? BeginAge { get; init; }
    public string? EndAge { get; init; }
}
