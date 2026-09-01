/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Core.ReferenceData;

/// <summary>
/// One &lt;observation&gt; entry from the Schedule file's own &lt;observations&gt; catalog (277
/// real entries) - a genuinely new addition, not previously parsed by anything in this codebase.
/// Distinct from Indication.ObservationCode/Description (§5.1, attached per-series to a specific
/// Risk-type series) - this is the Schedule file's own separate, deduplicated master catalog of
/// every real observation code, with richer detail (contraindication text, clarifying guidance,
/// and coded value mappings to SNOMED/CDCPHINVS) that individual series-level Indications don't
/// carry.
///
/// ObservationCode has real, meaningful leading zeros in the actual data ("001", "002", ...) -
/// same reasoning as CvxMapEntry.Cvx, so this stays a string, never parsed as a number.
///
/// Group is always empty in real data (confirmed: 0 of 277 entries have it populated) - kept as
/// a nullable field for structural completeness/forward-compatibility rather than omitted, since
/// a future CDC data drop could populate it even though the current one never does.
/// </summary>
public sealed class Observation
{
    public required string ObservationCode { get; init; }
    public required string ObservationTitle { get; init; }
    public string? Group { get; init; }
    public string? IndicationText { get; init; }
    public string? ContraindicationText { get; init; }
    public string? ClarifyingText { get; init; }
    public required IReadOnlyList<CodedValue> CodedValues { get; init; }
}

/// <summary>One &lt;codedValue&gt; - maps an observation to an external coding system (SNOMED, CDCPHINVS in real data). 157 of 277 real observations have at least one.</summary>
public sealed class CodedValue
{
    public required string Code { get; init; }
    public required string CodeSystem { get; init; }
    public string? Text { get; init; }
}
