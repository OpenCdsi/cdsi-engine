using Cdsi.Core.Common;
using Cdsi.Core.Models;

namespace Cdsi.Core.ReferenceData;

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

    /// <summary>§6.2 Evaluate Conditional Skip. Already filtered to context "Evaluation"/"Both" by the loader (per the spec's own instruction that Forecast-only instances don't apply here).</summary>
    public required IReadOnlyList<ConditionalSkipInstance> ConditionalSkipInstances { get; init; }
}

/// <summary>One &lt;preferableVaccine&gt; entry (Table 6-25). BeginAge/EndAge default to 1900-01-01/2999-12-31 when empty (Table 6-25's own "Assumed Value if Empty" column) — matched by CVX, same convention as every other vaccine-type comparison in this codebase (conflict rules, inadvertent vaccine).</summary>
public sealed class PreferableVaccine
{
    public required string Cvx { get; init; }
    public DurationExpression? BeginAge { get; init; }
    public DurationExpression? EndAge { get; init; }
    public string? TradeName { get; init; }
    public double? Volume { get; init; }

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

    private static readonly DateOnly DefaultFloor = new(1900, 1, 1);
    private static readonly DateOnly DefaultCeiling = new(2999, 12, 31);

    public DateOnly AbsMinAgeDate(DateOnly dob) => AbsMinAge?.AddTo(dob) ?? DefaultFloor;
    public DateOnly MinAgeDate(DateOnly dob) => MinAge?.AddTo(dob) ?? DefaultFloor;
    public DateOnly MaxAgeDate(DateOnly dob) => MaxAge?.AddTo(dob) ?? DefaultCeiling;
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
