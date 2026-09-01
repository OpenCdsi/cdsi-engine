/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Contracts.ReferenceData;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Api;

/// <summary>
/// GET /api/v3/observations/* - backed by the Schedule file's own dedicated &lt;observations&gt;
/// catalog (277 real entries), which nothing in this codebase parsed before this addition - see
/// Observation's own doc comment for why this is a genuinely new capability, and how it differs
/// from the per-series Indication.ObservationCode already used elsewhere.
///
/// ObservationCode lookups are exact-string, NOT case-insensitive, same reasoning as CVX code
/// lookups in VaccineEndpoints: these are opaque identifiers with real, meaningful leading zeros
/// ("001", "002", ...) in the actual data, not human-typed names.
/// </summary>
public static class ObservationEndpoints
{
    public static void MapObservationEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/observations", (ReferenceDataRepository data) => Results.Ok(GetSummaries(data)))
            .WithName("GetObservations")
            .WithTags("Supporting Data");

        group.MapGet("/observations/{code}", (string code, ReferenceDataRepository data) =>
        {
            return data.Schedule.ObservationsByCode.TryGetValue(code, out var observation)
                ? Results.Ok(ReferenceDataMapping.ToDto(observation))
                : Results.NotFound();
        })
            .WithName("GetObservationByCode")
            .WithTags("Supporting Data");
    }

    private static IReadOnlyList<ObservationSummaryDto> GetSummaries(ReferenceDataRepository data) =>
        data.Schedule.Observations
            .Select(ReferenceDataMapping.ToSummaryDto)
            .OrderBy(o => o.ObservationCode, StringComparer.Ordinal)
            .ToArray();
}
