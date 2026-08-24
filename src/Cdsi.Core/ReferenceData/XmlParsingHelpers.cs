/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using System.Globalization;
using System.Xml.Linq;
using Cdsi.Core.Common;
using Cdsi.Core.Models;

namespace Cdsi.Core.ReferenceData;

/// <summary>Small shared helpers for reading the loosely-typed (everything-is-a-string) CDSi XML supporting data.</summary>
internal static class XmlParsingHelpers
{
    /// <summary>Reads a child element's text, treating a missing element or a self-closing/empty element (e.g. &lt;maxAge/&gt;) both as "no value" — the supporting data uses empty elements rather than omitting them.</summary>
    public static string? ElementTextOrNull(this XElement parent, string name)
    {
        var text = parent.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static DurationExpression? ParseDurationOrNull(this XElement parent, string name)
    {
        var text = parent.ElementTextOrNull(name);
        return text is null ? null : DurationExpression.Parse(text);
    }

    /// <summary>Dates in this supporting data are formatted "yyyyMMdd" (e.g. "20230912").</summary>
    public static DateOnly? ParseDateOrNull(this XElement parent, string name)
    {
        var text = parent.ElementTextOrNull(name);
        if (text is null)
        {
            return null;
        }
        return DateOnly.ParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture);
    }

    public static Gender ParseGender(string text) => text.Trim().ToLowerInvariant() switch
    {
        "male" => Gender.Male,
        "female" => Gender.Female,
        "unknown" => Gender.Unknown,
        _ => throw new FormatException($"Unrecognized gender value: '{text}'")
    };

    public static SeriesType ParseSeriesType(string text) => text.Trim().ToLowerInvariant() switch
    {
        "standard" => SeriesType.Standard,
        "risk" => SeriesType.Risk,
        "evaluation only" => SeriesType.EvaluationOnly,
        _ => throw new FormatException($"Unrecognized series type: '{text}'")
    };

    /// <summary>Dose numbers are stored as free text like "Dose 1" — extract the trailing integer.</summary>
    public static int ParseDoseNumber(string text)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var n))
        {
            throw new FormatException($"Unable to extract a dose number from '{text}'");
        }
        return n;
    }

    /// <summary>fromMostRecent is a semicolon-delimited CVX list, e.g. "133; 215; 216".</summary>
    public static IReadOnlyList<string> ParseCvxList(string text) =>
        text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
}
