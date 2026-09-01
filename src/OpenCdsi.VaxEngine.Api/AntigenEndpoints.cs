/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Contracts.ReferenceData;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Api;

/// <summary>
/// GET /api/v3/antigens/* - mirrors the shape of an existing NodeJS "CDSi Supporting Data API"
/// this project is replicating (list/by-name/series/series-by-id/contraindications).
///
/// /antigens returns a lightweight summary (name + series count) for every antigen, not full
/// series detail for all 30 at once - the real, detailed series data (deeply nested
/// age/interval/vaccine-type/conditional-skip rules) is available per antigen via
/// /antigens/{name}/series. Worth reconsidering if the real NodeJS version's /antigens response
/// is closer to /antigens/{name} per entry - flagged here rather than assumed silently.
///
/// Antigen name lookups are case-insensitive (a browsable reference API's own names, like "HepA"
/// vs "hepa", shouldn't require exact-case recall) - CVX code lookups in the vaccine endpoints
/// are NOT, since those are opaque numeric-string identifiers, not names.
/// </summary>
public static class AntigenEndpoints
{
    public static void MapAntigenEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/antigens", (ReferenceDataRepository data) => Results.Ok(GetSummaries(data)))
            .WithName("GetAntigens")
            .WithTags("Supporting Data");

        group.MapGet("/antigens/{name}", (string name, ReferenceDataRepository data) =>
        {
            var summary = GetSummaries(data).SingleOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            return summary is not null ? Results.Ok(summary) : Results.NotFound();
        })
            .WithName("GetAntigenByName")
            .WithTags("Supporting Data");

        group.MapGet("/antigens/{name}/series", (string name, ReferenceDataRepository data) =>
        {
            var series = FindSeriesForAntigen(data, name);
            return series is not null
                ? Results.Ok(series.Select(ReferenceDataMapping.ToDto).ToArray())
                : Results.NotFound();
        })
            .WithName("GetAntigenSeries")
            .WithTags("Supporting Data");

        group.MapGet("/antigens/{name}/series/{id}", (string name, string id, ReferenceDataRepository data) =>
        {
            var series = FindSeriesForAntigen(data, name);
            if (series is null)
            {
                return Results.NotFound();
            }

            // id is "the index or name of the desired series" per this API's own spec - try a
            // numeric index first (1-based, matching how a human would naturally refer to "the
            // 2nd series" - confirmed against the real data's own ordering, which is load order,
            // not alphabetical), then fall back to matching the series's own real name.
            AntigenSeries? match = null;
            if (int.TryParse(id, out var index) && index >= 1 && index <= series.Count)
            {
                match = series[index - 1];
            }
            else
            {
                match = series.FirstOrDefault(s => string.Equals(s.SeriesName, id, StringComparison.OrdinalIgnoreCase));
            }

            return match is not null ? Results.Ok(ReferenceDataMapping.ToDto(match)) : Results.NotFound();
        })
            .WithName("GetAntigenSeriesById")
            .WithTags("Supporting Data");

        group.MapGet("/antigens/{name}/contraindications", (string name, ReferenceDataRepository data) =>
        {
            var matchedAntigen = data.ContraindicationsByAntigen.Keys
                .FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return matchedAntigen is not null
                ? Results.Ok(ReferenceDataMapping.ToDto(data.ContraindicationsByAntigen[matchedAntigen]))
                : Results.NotFound();
        })
            .WithName("GetAntigenContraindications")
            .WithTags("Supporting Data");
    }

    private static IReadOnlyList<AntigenSummaryDto> GetSummaries(ReferenceDataRepository data) =>
        data.AllSeries
            .GroupBy(s => s.Antigen)
            .Select(g => ReferenceDataMapping.ToSummaryDto(g.Key, g.Count()))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<AntigenSeries>? FindSeriesForAntigen(ReferenceDataRepository data, string antigenName)
    {
        var matches = data.AllSeries.Where(s => string.Equals(s.Antigen, antigenName, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length > 0 ? matches : null;
    }
}
