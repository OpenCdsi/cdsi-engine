/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Common;
using OpenCdsi.VaxEngine.Core.Models;

namespace OpenCdsi.VaxEngine.Core.ReferenceData;

public enum SeriesType
{
    Standard,
    Risk,
    EvaluationOnly
}

/// <summary>One &lt;series&gt; element from an antigen supporting-data file (e.g. "Hib risk child 2-dose series").</summary>
public sealed class AntigenSeries
{
    public required string SeriesName { get; init; }
    public required string Antigen { get; init; }
    public string? TargetDisease { get; init; }
    public string? VaccineGroup { get; init; }
    public required SeriesType SeriesType { get; init; }

    /// <summary>Genders this series applies to. Real data uses a single empty &lt;requiredGender/&gt; element to mean "no restriction" (Table 5-2: "Assumed Value if Empty: Gender of the patient" — i.e. it always matches) rather than omitting the element or listing all three values explicitly. A genuine restriction (e.g. HPV: Female, Unknown) is represented as an explicit non-empty list. See AppliesToGender.</summary>
    public required IReadOnlyList<Gender> RequiredGenders { get; init; }

    public required IReadOnlyList<Indication> Indications { get; init; }
    public required IReadOnlyList<SeriesDose> SeriesDoses { get; init; }

    /// <summary>§7.5 FORECASTGUIDANCE-1. Free-text administrative guidance for the series regimen itself (e.g. "Anyone age 60 years or older who does not meet risk-based recommendations may still receive Hepatitis B vaccination."). Only non-empty entries kept - real data has 250 total, 211 non-empty.</summary>
    public required IReadOnlyList<string> SeriesAdminGuidance { get; init; }

    /// <summary>§8.1+ Chapter 8 concepts, from §5.1's &lt;selectSeries&gt; element.</summary>
    public required SeriesGroupInfo SeriesGroupInfo { get; init; }

    /// <summary>The OTHER series group ID whose completion can substitute for this series' own group (e.g. HepB's "Standard" group (1) and "Increased Risk" group (2) reference each other). Null if this series has no equivalent group. 54/143 real series have this populated.</summary>
    public string? EquivalentSeriesGroup { get; init; }

    public bool AppliesToGender(Gender patientGender) =>
        RequiredGenders.Count == 0 || RequiredGenders.Contains(patientGender);
}

/// <summary>A &lt;indication&gt; element — a risk condition/observation that makes a Risk-type series relevant for a patient (§5.1, Table 5-4).</summary>
public sealed class Indication
{
    public string? ObservationCode { get; init; }
    public string? Description { get; init; }
    public DurationExpression? BeginAge { get; init; }
    public DurationExpression? EndAge { get; init; }

    /// <summary>§7.5 FORECASTGUIDANCE-1. Free-text guidance specific to this indication. Real data: 791 total, 136 non-empty.</summary>
    public string? Guidance { get; init; }
}

/// <summary>
/// One &lt;seriesDose&gt; element. Age/Interval rules are modeled now (per our §6.4/§6.5/§6.6
/// walkthrough) so this is ready for the Chapter 6 evaluation engine, but they are NOT yet
/// evaluated by anything in this codebase — only CreateRelevantPatientSeries (§5.1) consumes
/// AntigenSeries today, and it doesn't look inside SeriesDose at all.
/// </summary>
public sealed class SeriesDose
{
    public required int DoseNumber { get; init; }
    public required IReadOnlyList<AgeRule> AgeRules { get; init; }
    public required IReadOnlyList<PreferableIntervalRule> PreferableIntervals { get; init; }
    public required IReadOnlyList<AllowableIntervalRule> AllowableIntervals { get; init; }

    /// <summary>§6.3 Evaluate For Inadvertent Vaccine (Table 6-12/6-13): CVX codes that, if administered for this target dose, count as an inadvertent administration rather than a real dose. Simple set membership — no temporal versioning, no reference-date resolution.</summary>
    public required IReadOnlyList<string> InadvertentVaccineCvxCodes { get; init; }

    /// <summary>§6.8 Evaluate Preferable Vaccine (Table 6-25/6-26).</summary>
    public required IReadOnlyList<PreferableVaccine> PreferableVaccines { get; init; }

    /// <summary>§6.9 Evaluate Allowable Vaccine (Table 6-28/6-29).</summary>
    public required IReadOnlyList<AllowableVaccine> AllowableVaccines { get; init; }

    /// <summary>§6.2 / §7.1 Evaluate Conditional Skip. ALL instances are loaded regardless of context - the Evaluation-vs-Forecast context filter is applied by the caller at evaluation time (see EvaluateConditionalSkip.CanBeSkipped's context parameter), not here, since the same supporting data serves both §6.2 (Evaluation) and §7.1 (Forecast) with only the applicable-context filter differing between them.</summary>
    public required IReadOnlyList<ConditionalSkipInstance> ConditionalSkipInstances { get; init; }

    /// <summary>§7.4 Determine Forecast Need (Table 7-9/7-10). At most one per dose in real data (53 populated instances across all 30 files). Null if this dose has no seasonal restriction.</summary>
    public SeasonalRecommendation? SeasonalRecommendation { get; init; }

    /// <summary>§4.4 step 5: "a dose that is to be repeated" (Td boosters, annual flu/COVID, occupational rabies exposure, etc.) - a required field on every real seriesDose, not optional. Real data: 484 total, 29 flagged "Yes", always the LAST dose of its series in the current dataset (though the spec's own change log confirms the algorithm now supports it on any target dose, not just the last).</summary>
    public required bool IsRecurringDose { get; init; }
}

/// <summary>A date window (e.g. a flu season) outside of which a dose shouldn't be forecast. EndDate defaults to 12/31/2999 when absent (Table 7-9) - real COVID-19 data only specifies StartDate, meaning it never "expires" under this rule.</summary>
public sealed class SeasonalRecommendation
{
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }

    public DateOnly EffectiveEndDate => EndDate ?? new DateOnly(2999, 12, 31);
}

/// <summary>One &lt;preferableVaccine&gt; entry (Table 6-25). BeginAge/EndAge default to 1900-01-01/2999-12-31 when empty (Table 6-25's own "Assumed Value if Empty" column) — matched by CVX, same convention as every other vaccine-type comparison in this codebase (conflict rules, inadvertent vaccine).</summary>
public sealed class PreferableVaccine
{
    public required string Cvx { get; init; }
    public DurationExpression? BeginAge { get; init; }
    public DurationExpression? EndAge { get; init; }
    public string? TradeName { get; init; }
    public double? Volume { get; init; }

    /// <summary>§7.5 FORECASTRECVAC-1. "Y" means this vaccine type can be recommended by the Forecast (not every preferable vaccine is forecast-eligible - 742 of 1089 real entries are "N"). Defaults to false ("N") when absent, per Table 7-12.</summary>
    public bool ForecastVaccineTypeFlag { get; init; }

    private static readonly DateOnly DefaultFloor = new(1900, 1, 1);
    private static readonly DateOnly DefaultCeiling = new(2999, 12, 31);

    public DateOnly BeginAgeDate(DateOnly dob) => BeginAge?.AddTo(dob) ?? DefaultFloor;
    public DateOnly EndAgeDate(DateOnly dob) => EndAge?.AddTo(dob) ?? DefaultCeiling;
}

/// <summary>One &lt;allowableVaccine&gt; entry (Table 6-28). Same age-default convention as PreferableVaccine, but no trade name/volume fields — Table 6-29 only checks vaccine type and age window.</summary>
public sealed class AllowableVaccine
{
    public required string Cvx { get; init; }
    public DurationExpression? BeginAge { get; init; }
    public DurationExpression? EndAge { get; init; }

    private static readonly DateOnly DefaultFloor = new(1900, 1, 1);
    private static readonly DateOnly DefaultCeiling = new(2999, 12, 31);

    public DateOnly BeginAgeDate(DateOnly dob) => BeginAge?.AddTo(dob) ?? DefaultFloor;
    public DateOnly EndAgeDate(DateOnly dob) => EndAge?.AddTo(dob) ?? DefaultCeiling;
}

/// <summary>§6.4 Evaluate Age. Table 6-14 empty-value defaults: AbsMinAge/MinAge missing → 1900-01-01 floor; MaxAge missing → 2999-12-31 ceiling.</summary>
public sealed class AgeRule : ITemporallyVersioned
{
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? CessationDate { get; init; }
    public DurationExpression? AbsMinAge { get; init; }
    public DurationExpression? MinAge { get; init; }
    public DurationExpression? MaxAge { get; init; }

    /// <summary>§7.5 CALCDTAGE-3/CALCDTAGE-2. Forecast-only concepts (not used by §6.4 Ch.6 evaluation) - no "Assumed Value if Empty" default per Table 7-12, so these stay null rather than falling back to a sentinel when absent.</summary>
    public DurationExpression? EarliestRecAge { get; init; }
    public DurationExpression? LatestRecAge { get; init; }

    private static readonly DateOnly DefaultFloor = new(1900, 1, 1);
    private static readonly DateOnly DefaultCeiling = new(2999, 12, 31);

    public DateOnly AbsMinAgeDate(DateOnly dob) => AbsMinAge?.AddTo(dob) ?? DefaultFloor;
    public DateOnly MinAgeDate(DateOnly dob) => MinAge?.AddTo(dob) ?? DefaultFloor;
    public DateOnly MaxAgeDate(DateOnly dob) => MaxAge?.AddTo(dob) ?? DefaultCeiling;

    /// <summary>Null if EarliestRecAge isn't specified - per §7.5, callers must NOT default this to a sentinel.</summary>
    public DateOnly? EarliestRecAgeDate(DateOnly dob) => EarliestRecAge?.AddTo(dob);
    public DateOnly? LatestRecAgeDate(DateOnly dob) => LatestRecAge?.AddTo(dob);
}

public enum IntervalReferenceType
{
    FromPrevious,
    FromTargetDose,
    FromMostRecent,
    FromRelevantObservation
}

/// <summary>§6.5 Evaluate Preferable Interval.</summary>
public sealed class PreferableIntervalRule : ITemporallyVersioned
{
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? CessationDate { get; init; }
    public required IntervalReferenceType ReferenceType { get; init; }
    public int? ReferenceTargetDoseNumber { get; init; }

    /// <summary>Populated when ReferenceType is FromMostRecent — semicolon-delimited CVX list in the source data (e.g. "133; 215; 216"), parsed into individual codes.</summary>
    public IReadOnlyList<string> ReferenceVaccineCvxCodes { get; init; } = Array.Empty<string>();

    /// <summary>Populated when ReferenceType is FromRelevantObservation.</summary>
    public string? ReferenceObservationCode { get; init; }

    public DurationExpression? AbsMinInt { get; init; }
    public DurationExpression? MinInt { get; init; }

    /// <summary>§7.5 CALCDTINT-5/CALCDTINT-6. Forecast-only concepts (not used by §6.5 Ch.6 evaluation) - no default per Table 7-12, so these stay null rather than falling back to a sentinel when absent.</summary>
    public DurationExpression? EarliestRecInt { get; init; }
    public DurationExpression? LatestRecInt { get; init; }

    /// <summary>
    /// §9.3 FORECASTPRIORITY-1's "interval priority flag." TERMINOLOGY MISMATCH worth knowing:
    /// the spec text describes this as a flag "of 'Y'", but real data never uses "Y" at all -
    /// swept all 490 real interval rules across all 30 files and found only two values: absent
    /// (460) or the literal string "override" (30). IsPriorityOverride treats "override" as the
    /// real-world equivalent of the spec's described "Y" state, since it's the only non-empty
    /// value that ever appears - an inference grounded in the data, not a quoted definition.
    /// </summary>
    public string? IntervalPriority { get; init; }

    public bool IsPriorityOverride => IntervalPriority == "override";

    /// <summary>Requires a resolved reference date, same as MinInt/AbsMinInt - see EvaluatePreferableInterval's reference-date resolution.</summary>
    public DateOnly? EarliestRecIntDate(DateOnly referenceDate) => EarliestRecInt?.AddTo(referenceDate);
    public DateOnly? LatestRecIntDate(DateOnly referenceDate) => LatestRecInt?.AddTo(referenceDate);
}

/// <summary>§6.6 Evaluate Allowable Interval — narrower than PreferableIntervalRule: no grace-period tier, and per §6.6, ABSENCE of this rule on a target dose means "not valid" rather than "valid" (opposite default from Age).</summary>
public sealed class AllowableIntervalRule : ITemporallyVersioned
{
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? CessationDate { get; init; }
    public required IntervalReferenceType ReferenceType { get; init; }
    public int? ReferenceTargetDoseNumber { get; init; }
    public DurationExpression? AbsMinInt { get; init; }
}
