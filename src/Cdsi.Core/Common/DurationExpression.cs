using System.Text.RegularExpressions;

namespace Cdsi.Core.Common;

/// <summary>
/// Parses the compound duration expressions used throughout the CDSi supporting data
/// (e.g. "6 months", "8 weeks - 4 days", "0 days"). These are NOT ISO-8601 durations;
/// they are a simple "&lt;n&gt; &lt;unit&gt;" optionally followed by a "- &lt;n&gt; &lt;unit&gt;"
/// subtraction, per the Logic Spec's plain-language age/interval attributes.
/// </summary>
public sealed partial class DurationExpression
{
    private readonly int _primaryValue;
    private readonly DurationUnit _primaryUnit;
    private readonly int? _subtractValue;
    private readonly DurationUnit? _subtractUnit;

    private DurationExpression(int primaryValue, DurationUnit primaryUnit, int? subtractValue, DurationUnit? subtractUnit)
    {
        _primaryValue = primaryValue;
        _primaryUnit = primaryUnit;
        _subtractValue = subtractValue;
        _subtractUnit = subtractUnit;
    }

    // e.g. "6 months - 4 days" ; "8 weeks"; "0 days"
    [GeneratedRegex(@"^\s*(?<n1>\d+)\s+(?<u1>day|days|week|weeks|month|months|year|years)\s*(-\s*(?<n2>\d+)\s+(?<u2>day|days|week|weeks|month|months|year|years))?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out DurationExpression? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = Pattern().Match(text);
        if (!match.Success)
        {
            throw new FormatException($"Unrecognized duration expression: '{text}'");
        }

        var primaryValue = int.Parse(match.Groups["n1"].Value);
        var primaryUnit = ParseUnit(match.Groups["u1"].Value);

        int? subtractValue = null;
        DurationUnit? subtractUnit = null;
        if (match.Groups["n2"].Success)
        {
            subtractValue = int.Parse(match.Groups["n2"].Value);
            subtractUnit = ParseUnit(match.Groups["u2"].Value);
        }

        result = new DurationExpression(primaryValue, primaryUnit, subtractValue, subtractUnit);
        return true;
    }

    public static DurationExpression Parse(string text)
    {
        if (!TryParse(text, out var result) || result is null)
        {
            throw new FormatException($"Unable to parse duration expression: '{text}'");
        }
        return result;
    }

    private static DurationUnit ParseUnit(string unit) => unit.ToLowerInvariant() switch
    {
        "day" or "days" => DurationUnit.Days,
        "week" or "weeks" => DurationUnit.Weeks,
        "month" or "months" => DurationUnit.Months,
        "year" or "years" => DurationUnit.Years,
        _ => throw new FormatException($"Unrecognized duration unit: '{unit}'")
    };

    private static DateOnly AddUnit(DateOnly date, int value, DurationUnit unit) => unit switch
    {
        DurationUnit.Days => date.AddDays(value),
        DurationUnit.Weeks => date.AddDays(value * 7),
        DurationUnit.Months => date.AddMonths(value),
        DurationUnit.Years => date.AddYears(value),
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    /// <summary>Applies this duration to an anchor date (e.g. date of birth, a reference dose date).</summary>
    public DateOnly AddTo(DateOnly anchor)
    {
        var result = AddUnit(anchor, _primaryValue, _primaryUnit);
        if (_subtractValue is int sv && _subtractUnit is DurationUnit su)
        {
            result = AddUnit(result, -sv, su);
        }
        return result;
    }

    public override string ToString()
    {
        var s = $"{_primaryValue} {_primaryUnit}";
        if (_subtractValue is int sv && _subtractUnit is DurationUnit su)
        {
            s += $" - {sv} {su}";
        }
        return s;
    }

    private enum DurationUnit { Days, Weeks, Months, Years }
}
