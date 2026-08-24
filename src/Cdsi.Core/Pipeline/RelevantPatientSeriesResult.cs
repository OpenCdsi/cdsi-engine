/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Pipeline;

public sealed class RelevantPatientSeriesResult
{
    public required IReadOnlyList<AntigenSeries> RelevantSeries { get; init; }

    /// <summary>Per §5.1: Risk series where at least one indication was inconclusive and none unambiguously applied. Not evaluated/forecast, but surfaced for manual clinician review.</summary>
    public required IReadOnlyList<UnresolvedIndicationNotification> UnresolvedIndications { get; init; }
}

public sealed class UnresolvedIndicationNotification
{
    public required string SeriesName { get; init; }
    public required string Antigen { get; init; }
    public string? ObservationCode { get; init; }
    public string? Description { get; init; }
}
