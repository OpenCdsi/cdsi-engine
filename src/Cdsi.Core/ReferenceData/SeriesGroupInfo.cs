/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Common;

namespace Cdsi.Core.ReferenceData;

/// <summary>
/// §5.1's &lt;selectSeries&gt; element, needed starting in Chapter 8 - not parsed when
/// CreateRelevantPatientSeries was originally built, since nothing needed it until now. Present
/// on every real series (143/143, matches the XSD's required cardinality).
///
/// SeriesGroup is the group ID a series belongs to (e.g. HepB has group "1" = "Standard" and
/// group "2" = "Increased Risk", 17 series total split across them). SeriesPreference is a
/// tie-breaking rank within a group (1 = most preferred). EquivalentSeriesGroup (on
/// AntigenSeries, not here - see its own doc comment) cross-references a DIFFERENT group whose
/// completion can substitute for this one's.
/// </summary>
public sealed class SeriesGroupInfo
{
    public required bool IsDefaultSeries { get; init; }
    public required bool IsProductPath { get; init; }
    public required string SeriesGroupName { get; init; }
    public required string SeriesGroup { get; init; }

    /// <summary>Real data uses "A"/"B"/"C" - "A" is highest priority. Ordinal string comparison ("A" &lt; "B" &lt; "C") matches priority ordering directly, so no separate enum/int mapping is needed.</summary>
    public required string SeriesPriority { get; init; }

    /// <summary>Null in 12 of 143 real series (e.g. Shared Clinical Decision Making series where preference ranking doesn't apply) - genuinely absent, not a parsing gap.</summary>
    public int? SeriesPreference { get; init; }
    public DurationExpression? MinAgeToStart { get; init; }
    public DurationExpression? MaxAgeToStart { get; init; }

    public DateOnly? MinAgeToStartDate(DateOnly dob) => MinAgeToStart?.AddTo(dob);
    public DateOnly? MaxAgeToStartDate(DateOnly dob) => MaxAgeToStart?.AddTo(dob);
}
