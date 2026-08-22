using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §6.2 / §7.1 Evaluate Conditional Skip (Tables 6-6 through 6-11). Determines whether a target
/// dose can be skipped entirely. Like §6.1, this is a pre-gate: Table 6-11's outcome is a
/// distinct "Skipped" target dose status, not one of the AND-able conditions in §6.10's
/// aggregator, so results here are a plain bool rather than DoseEvaluationOutcome.
///
/// The exact same tables/business rules serve both §6.2 (Evaluation) and §7.1 (Forecast) - the
/// only difference is which `context` value on the top-level instance applies ("Evaluation"/
/// "Both" for §6.2, "Forecast"/"Both" for §7.1). This is why ConditionalSkipContext exists as a
/// caller-supplied parameter rather than being baked into the loader.
///
/// SCOPE NOTE: of the four condition types, "Completed Series" (Table 6-7) needs cross-series
/// status data that doesn't exist anywhere in this codebase yet (no series-level completion
/// tracking has been built). Its result is caller-supplied via a resolver delegate, same pattern
/// as Interval's reference-date resolution. Age, Interval, and Vaccine Count are fully
/// implemented against real business rules.
/// </summary>
public enum ConditionalSkipContext { Evaluation, Forecast }

public static class EvaluateConditionalSkip
{
    /// <param name="referenceDate">Evaluation context (§6.2/CONDSKIP-2): date administered. Forecast context: the assessment date - this is the caller's responsibility to supply correctly for the context requested.</param>
    /// <param name="context">Which caller this is for - filters which top-level instances apply (§6.2 uses Evaluation/Both; §7.1 uses Forecast/Both).</param>
    /// <param name="priorDosesOfThisAntigen">This antigen's own prior administered doses, chronologically unordered is fine - used for Table 6-8's "immediate previous dose" and Table 6-9's counting.</param>
    /// <param name="resolveCompletedSeries">Caller-supplied: given a condition's SeriesGroups value, does that group have at least one relevant patient series with status 'Complete'? Not resolvable internally - see scope note above.</param>
    public static bool CanBeSkipped(
        DateOnly dateOfBirth,
        DateOnly referenceDate,
        ConditionalSkipContext context,
        IReadOnlyList<ConditionalSkipInstance> instances,
        IReadOnlyList<PriorVaccineDoseAdministered> priorDosesOfThisAntigen,
        Func<string?, bool> resolveCompletedSeries)
    {
        var applicableInstances = instances.Where(i => InstanceAppliesToContext(i, context)).ToArray();

        // ASSUMPTION (see ConditionalSkipInstance's doc comment): multiple top-level instances OR together.
        return applicableInstances.Any(instance => InstanceCanSkip(dateOfBirth, referenceDate, instance, priorDosesOfThisAntigen, resolveCompletedSeries));
    }

    private static bool InstanceAppliesToContext(ConditionalSkipInstance instance, ConditionalSkipContext context)
    {
        if (instance.Context is null)
        {
            return true; // no context specified - not observed in real data, but fail open rather than silently excluding it
        }
        if (instance.Context.Equals("Both", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var expected = context == ConditionalSkipContext.Evaluation ? "Evaluation" : "Forecast";
        return instance.Context.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool InstanceCanSkip(
        DateOnly dateOfBirth, DateOnly referenceDate, ConditionalSkipInstance instance,
        IReadOnlyList<PriorVaccineDoseAdministered> priorDoses, Func<string?, bool> resolveCompletedSeries)
    {
        var applicableSets = instance.Sets.Where(s => s.IsApplicable(referenceDate)).ToArray();

        // Table 6-11: no applicable sets -> "None" -> cannot be skipped, regardless of SetLogic.
        if (applicableSets.Length == 0)
        {
            return false;
        }

        var setResults = applicableSets.Select(s => SetIsMet(dateOfBirth, referenceDate, s, priorDoses, resolveCompletedSeries)).ToArray();

        return instance.SetLogic switch
        {
            SkipCombinationLogic.And => setResults.All(met => met),
            SkipCombinationLogic.Or => setResults.Any(met => met),
            null => setResults.Length == 1 && setResults[0], // single set, no combination logic needed
            _ => throw new InvalidOperationException($"Unhandled {nameof(SkipCombinationLogic)} value.")
        };
    }

    private static bool SetIsMet(
        DateOnly dateOfBirth, DateOnly referenceDate, ConditionalSkipSet set,
        IReadOnlyList<PriorVaccineDoseAdministered> priorDoses, Func<string?, bool> resolveCompletedSeries)
    {
        var conditionResults = set.Conditions
            .Select(c => ConditionIsMet(dateOfBirth, referenceDate, c, priorDoses, resolveCompletedSeries))
            .ToArray();

        return set.ConditionLogic switch
        {
            SkipCombinationLogic.And => conditionResults.All(met => met),
            SkipCombinationLogic.Or => conditionResults.Any(met => met),
            null => conditionResults.Length == 1 && conditionResults[0], // single condition, no combination logic needed
            _ => throw new InvalidOperationException($"Unhandled {nameof(SkipCombinationLogic)} value.")
        };
    }

    private static bool ConditionIsMet(
        DateOnly dateOfBirth, DateOnly referenceDate, ConditionalSkipCondition condition,
        IReadOnlyList<PriorVaccineDoseAdministered> priorDoses, Func<string?, bool> resolveCompletedSeries) => condition.ConditionType switch
    {
        ConditionType.Age => EvaluateAgeCondition(dateOfBirth, referenceDate, condition),
        ConditionType.Interval => EvaluateIntervalCondition(referenceDate, condition, priorDoses),
        ConditionType.VaccineCount => EvaluateVaccineCountCondition(dateOfBirth, condition, priorDoses),
        ConditionType.CompletedSeries => resolveCompletedSeries(condition.SeriesGroups),
        _ => throw new InvalidOperationException($"Unhandled {nameof(ConditionType)} value.")
    };

    /// <summary>Table 6-6.</summary>
    private static bool EvaluateAgeCondition(DateOnly dateOfBirth, DateOnly referenceDate, ConditionalSkipCondition condition)
    {
        var beginDate = condition.BeginAgeDate(dateOfBirth);
        var endDate = condition.EndAgeDate(dateOfBirth);
        return referenceDate >= beginDate && referenceDate < endDate;
    }

    /// <summary>Table 6-8. CALCDTSKIP-5: conditional skip interval date = immediate previous dose's date administered + the condition's interval.</summary>
    private static bool EvaluateIntervalCondition(DateOnly referenceDate, ConditionalSkipCondition condition, IReadOnlyList<PriorVaccineDoseAdministered> priorDoses)
    {
        if (priorDoses.Count == 0)
        {
            return false; // "Has at least one dose been administered?" -> No -> condition not met.
        }
        var mostImmediatePrevious = priorDoses.OrderByDescending(d => d.DateAdministered).First();
        if (condition.Interval is null)
        {
            return false; // no interval to compare against - shouldn't happen for a well-formed Interval-type condition, but fail safe rather than throw.
        }
        var conditionalSkipIntervalDate = condition.Interval.AddTo(mostImmediatePrevious.DateAdministered);
        return referenceDate >= conditionalSkipIntervalDate;
    }

    /// <summary>Table 6-9 + CONDSKIP-1. Counts prior doses matching vaccine type, age window, and date window, filtered by evaluation status per DoseType, then compares the count against DoseCount per DoseCountLogic.</summary>
    private static bool EvaluateVaccineCountCondition(DateOnly dateOfBirth, ConditionalSkipCondition condition, IReadOnlyList<PriorVaccineDoseAdministered> priorDoses)
    {
        var beginAgeDate = condition.BeginAgeDate(dateOfBirth);
        var endAgeDate = condition.EndAgeDate(dateOfBirth);

        var matchingCount = priorDoses.Count(dose =>
            (condition.VaccineTypeCvxCodes.Count == 0 || condition.VaccineTypeCvxCodes.Contains(dose.Cvx)) &&
            dose.DateAdministered >= beginAgeDate && dose.DateAdministered < endAgeDate &&
            dose.DateAdministered >= condition.EffectiveStartDate && dose.DateAdministered < condition.EffectiveEndDate &&
            (condition.DoseType != ConditionalSkipDoseType.Valid || dose.EvaluationStatus == PriorDoseEvaluationStatus.Valid));

        var requiredCount = condition.DoseCount ?? 0;

        return condition.DoseCountLogic switch
        {
            DoseCountLogic.GreaterThan => matchingCount > requiredCount,
            DoseCountLogic.EqualTo => matchingCount == requiredCount,
            DoseCountLogic.LessThan => matchingCount < requiredCount,
            null => false, // no comparison operator specified - shouldn't happen for a well-formed condition, fail safe.
            _ => throw new InvalidOperationException($"Unhandled {nameof(DoseCountLogic)} value.")
        };
    }
}
