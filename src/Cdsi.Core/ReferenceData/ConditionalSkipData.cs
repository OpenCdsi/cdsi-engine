using Cdsi.Core.Common;

namespace Cdsi.Core.ReferenceData;

/// <summary>
/// §6.2 Evaluate Conditional Skip. One &lt;conditionalSkip&gt; element — a seriesDose can have
/// more than one (max observed in real data: 2). The spec's Table 6-10/6-11 define how Sets and
/// Conditions combine WITHIN one instance, but not how multiple top-level instances combine
/// with each other. ASSUMPTION (not spec-grounded, flagged for review): multiple instances are
/// OR'd — the target dose can be skipped if ANY instance says it can, matching the framing that
/// a dose is skippable only when explicitly determined to be, so multiple independent skip
/// criteria should each be sufficient on their own.
/// </summary>
public sealed class ConditionalSkipInstance
{
    public required string? Context { get; init; } // "Evaluation" | "Forecast" | "Both" - already filtered to Evaluation/Both by the loader
    public required SkipCombinationLogic? SetLogic { get; init; }
    public required IReadOnlyList<ConditionalSkipSet> Sets { get; init; }
}

/// <summary>One &lt;set&gt;. Temporal applicability (EffectiveDate/CessationDate) is a simple boolean date-range filter here, NOT a "select exactly one" pattern like TemporalRuleSelector — multiple sets can be simultaneously applicable, and Table 6-11 counts across all of them.</summary>
public sealed class ConditionalSkipSet
{
    public string? SetId { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? CessationDate { get; init; }
    public required SkipCombinationLogic? ConditionLogic { get; init; }
    public required IReadOnlyList<ConditionalSkipCondition> Conditions { get; init; }

    private static readonly DateOnly DistantPast = new(1, 1, 1);
    private static readonly DateOnly DistantFuture = DateOnly.MaxValue;

    public bool IsApplicable(DateOnly anchorDate) =>
        (EffectiveDate ?? DistantPast) <= anchorDate && anchorDate < (CessationDate ?? DistantFuture);
}

public enum SkipCombinationLogic { And, Or }

/// <summary>
/// The real data uses several inconsistently-cased condition-type strings that all map onto
/// the same Table 6-9 "Vaccine Count" logic ("Vaccine Count by Age", "Vaccine Count By Date",
/// "Vaccine Count by Date and Age", etc.) - CONDSKIP-1's counting rule always applies both an
/// age-window AND a date-window filter (using Table 6-4's own empty defaults when one isn't
/// meaningful for a given condition), so unifying them into one ConditionType.VaccineCount is a
/// behavior-preserving simplification, not a shortcut that changes results.
/// </summary>
public enum ConditionType { Age, CompletedSeries, Interval, VaccineCount }

public enum DoseCountLogic { GreaterThan, EqualTo, LessThan }
public enum ConditionalSkipDoseType { Valid, Total }

/// <summary>One &lt;condition&gt;. Which fields are meaningful depends on ConditionType (see EvaluateConditionalSkip for the mapping to Tables 6-6/6-7/6-8/6-9).</summary>
public sealed class ConditionalSkipCondition
{
    public required ConditionType ConditionType { get; init; }

    // Vaccine Count fields (Table 6-9 / CONDSKIP-1)
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public DurationExpression? BeginAge { get; init; }
    public DurationExpression? EndAge { get; init; }
    public int? DoseCount { get; init; }
    public ConditionalSkipDoseType? DoseType { get; init; }
    public DoseCountLogic? DoseCountLogic { get; init; }
    public IReadOnlyList<string> VaccineTypeCvxCodes { get; init; } = Array.Empty<string>();

    // Interval fields (Table 6-8)
    public DurationExpression? Interval { get; init; }

    // Completed Series fields (Table 6-7) - not resolvable without cross-series data yet
    public string? SeriesGroups { get; init; }

    private static readonly DateOnly DefaultFloor = new(1900, 1, 1);
    private static readonly DateOnly DefaultCeiling = new(2999, 12, 31);

    public DateOnly BeginAgeDate(DateOnly dob) => BeginAge?.AddTo(dob) ?? DefaultFloor;
    public DateOnly EndAgeDate(DateOnly dob) => EndAge?.AddTo(dob) ?? DefaultCeiling;
    public DateOnly EffectiveStartDate => StartDate ?? DefaultFloor;
    public DateOnly EffectiveEndDate => EndDate ?? DefaultCeiling;
}
