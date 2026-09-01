/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Common;

namespace OpenCdsi.VaxEngine.Core.ReferenceData;

/// <summary>
/// One &lt;liveVirusConflict&gt; entry from the Schedule supporting data (§6.7). Despite the
/// XML element name, this table covers both live-virus and non-live-virus conflicts (per the
/// Logic Spec's implementer note — the tag name is a legacy holdover). Terminology mapping:
/// spec's "Conflicting Vaccine Type" = XML's &lt;previous&gt;; spec's "Impacted Vaccine Type" = XML's &lt;current&gt;.
/// Not yet consumed by any pipeline stage in this codebase — modeled now for the Chapter 6 build-out.
/// </summary>
public sealed class VaccineConflictRule
{
    public required string ConflictingVaccineType { get; init; }
    public required string ConflictingCvx { get; init; }
    public required string ImpactedVaccineType { get; init; }
    public required string ImpactedCvx { get; init; }
    public required DurationExpression ConflictBeginInterval { get; init; }
    public required DurationExpression MinConflictEndInterval { get; init; }
    public required DurationExpression ConflictEndInterval { get; init; }
}
