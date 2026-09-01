/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using System.Text.RegularExpressions;

namespace OpenCdsi.VaxEngine.Core.Common;

/// <summary>
/// Parses the compound duration expressions used throughout the CDSi supporting data
/// (e.g. "6 months", "8 weeks - 4 days", "8 months + 1 day", "0 days"). These are NOT
/// ISO-8601 durations; they are a simple "&lt;n&gt; &lt;unit&gt;" optionally followed by a
/// "+" or "-" "&lt;n&gt; &lt;unit&gt;" adjustment, per the Logic Spec's plain-language
/// age/interval attributes. Both operators appear in the real data (e.g. Rotavirus maxAge
/// uses "8 months + 1 day") — don't assume subtraction is the only case.
/// </summary>
public sealed partial class DurationExpression
{
    private readonly int _primaryValue;
    private readonly DurationUnit _primaryUnit;
    private readonly int? _adjustValue; // signed: positive for "+", negative for "-"
    private readonly DurationUnit? _adjustUnit;

    private DurationExpression(int primaryValue, DurationUnit primaryUnit, int? adjustValue, DurationUnit? adjustUnit)
    {
        _primaryValue = primaryValue;
        _primaryUnit = primaryUnit;
        _adjustValue = adjustValue;
        _adjustUnit = adjustUnit;
    }

    // e.g. "6 months - 4 days" ; "8 months + 1 day" ; "8 weeks"; "0 days"
    [GeneratedRegex(@"^\s*(?<n1>\d+)\s+(?<u1>day|days|week|weeks|month|months|year|years)\s*((?<op>[+-])\s*(?<n2>\d+)\s+(?<u2>day|days|week|weeks|month|months|year|years))?\s*$",
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

        int? adjustValue = null;
        DurationUnit? adjustUnit = null;
        if (match.Groups["n2"].Success)
        {
            var magnitude = int.Parse(match.Groups["n2"].Value);
            adjustValue = match.Groups["op"].Value == "-" ? -magnitude : magnitude;
            adjustUnit = ParseUnit(match.Groups["u2"].Value);
        }

        result = new DurationExpression(primaryValue, primaryUnit, adjustValue, adjustUnit);
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
        DurationUnit.Months => AddMonthsWithRollover(date, value),
        DurationUnit.Years => AddMonthsWithRollover(date, value * 12),
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    /// <summary>
    /// REAL BUG, FOUND AND FIXED - found via real corpus cases 2013-0003/2013-0130/2013-0165
    /// (DTaP-family, DOB 2026-05-31, dose 3 recommendedDate: expected 2026-12-01, got
    /// 2026-11-30) - a single, isolated, one-day mismatch, but genuinely spec-confirmed and
    /// wide-reaching, since every age/interval calculation in this project involving months or
    /// years goes through this one function.
    ///
    /// .NET's own DateOnly.AddMonths/AddYears CLAMP when the source day-of-month doesn't exist
    /// in the target month (May 31 + 6 months = November 30, since November only has 30 days) -
    /// standard, unsurprising .NET behavior, but it directly contradicts CALCDT-5's own explicit
    /// text and worked examples: "A computed date which is not a real date must be moved forward
    /// to first day of the next month" - "03/31/2000 + 6 months = 10/01/2000 (September 31 does
    /// not exist)" and "08/31/2010 + 6 months = 03/01/2011 (February 31 does not exist)". The
    /// spec wants ROLLOVER to the 1st of the month AFTER the invalid one, not clamping down to
    /// the invalid month's own last real day.
    ///
    /// Fixed by detecting clamping directly (compare the naive .NET result's own day-of-month
    /// against the anchor's) rather than trying to independently reimplement "is this a real
    /// date" logic, and rolling forward to the 1st of the following month when it occurred -
    /// matching CALCDT-5's own worked examples exactly (both hand-verified: 03/31/2000 + 6
    /// months and 08/31/2010 + 6 months both reproduce the spec's own stated results). Applied to
    /// years too, not just months, since the identical "source day doesn't exist in the target
    /// month" problem can occur there too (e.g. Feb 29 + 1 year landing on a non-leap year) - the
    /// spec's own CALCDT-1/CALCDT-2 pairing treats year-then-month adjustment as the same kind of
    /// operation, and nothing in CALCDT-5's own text scopes the "not a real date" rule to months
    /// only. Implemented by counting years as 12 months, reusing one code path.
    /// </summary>
    private static DateOnly AddMonthsWithRollover(DateOnly date, int months)
    {
        var naive = date.AddMonths(months);
        if (naive.Day != date.Day)
        {
            // Clamped - .NET rounded down to the target month's own last real day because the
            // anchor's day-of-month doesn't exist there. Roll forward to the 1st of the
            // following month instead, per CALCDT-5.
            return new DateOnly(naive.Year, naive.Month, 1).AddMonths(1);
        }
        return naive;
    }

    /// <summary>Applies this duration to an anchor date (e.g. date of birth, a reference dose date).</summary>
    public DateOnly AddTo(DateOnly anchor)
    {
        var result = AddUnit(anchor, _primaryValue, _primaryUnit);
        if (_adjustValue is int av && _adjustUnit is DurationUnit au)
        {
            result = AddUnit(result, av, au); // av already carries its sign (+ or -)
        }
        return result;
    }

    public override string ToString()
    {
        var s = $"{_primaryValue} {_primaryUnit}";
        if (_adjustValue is int av && _adjustUnit is DurationUnit au)
        {
            s += av >= 0 ? $" + {av} {au}" : $" - {-av} {au}";
        }
        return s;
    }

    private enum DurationUnit { Days, Weeks, Months, Years }
}
