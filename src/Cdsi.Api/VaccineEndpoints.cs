/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Contracts.ReferenceData;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Api;

/// <summary>
/// GET /api/v3/vaccines/* - CVX code lookups are exact-string, NOT case-insensitive (unlike the
/// name-based antigen/vaccine-group lookups) - CVX codes are opaque numeric-string identifiers,
/// not human-typed names, so there's no meaningful "case" to be lenient about.
///
/// "Conflicts for this vaccine" uses ConflictsByImpactedCvx specifically - the real §6.7
/// direction (see ScheduleSupportingData's own doc comment): "given the dose I'm evaluating,
/// which PRIOR vaccine types could conflict with it," not the reverse. Asking for CVX 03's own
/// conflicts means "what could conflict with giving CVX 03," which is exactly what
/// ConflictsByImpactedCvx already indexes.
/// </summary>
public static class VaccineEndpoints
{
    public static void MapVaccineEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/vaccines", (ReferenceDataRepository data) => Results.Ok(GetSummaries(data)))
            .WithName("GetVaccines")
            .WithTags("Supporting Data");

        group.MapGet("/vaccines/{cvx}", (string cvx, ReferenceDataRepository data) =>
        {
            return data.Schedule.CvxToAntigen.TryGetValue(cvx, out var entry)
                ? Results.Ok(ReferenceDataMapping.ToDto(entry))
                : Results.NotFound();
        })
            .WithName("GetVaccineByCvx")
            .WithTags("Supporting Data");

        group.MapGet("/vaccines/{cvx}/conflicts", (string cvx, ReferenceDataRepository data) =>
        {
            if (!data.Schedule.CvxToAntigen.ContainsKey(cvx))
            {
                return Results.NotFound();
            }
            var conflicts = data.Schedule.ConflictsByImpactedCvx.TryGetValue(cvx, out var rules)
                ? rules
                : Array.Empty<VaccineConflictRule>();
            return Results.Ok(conflicts.Select(ReferenceDataMapping.ToDto).ToArray());
        })
            .WithName("GetVaccineConflicts")
            .WithTags("Supporting Data");

        group.MapGet("/vaccines/{cvx}/antigens", (string cvx, ReferenceDataRepository data) =>
        {
            return data.Schedule.CvxToAntigen.TryGetValue(cvx, out var entry)
                ? Results.Ok(entry.Associations.Select(a => a.Antigen).ToArray())
                : Results.NotFound();
        })
            .WithName("GetVaccineAntigens")
            .WithTags("Supporting Data");
    }

    private static IReadOnlyList<VaccineSummaryDto> GetSummaries(ReferenceDataRepository data) =>
        data.Schedule.CvxToAntigen.Values
            .Select(ReferenceDataMapping.ToSummaryDto)
            .OrderBy(v => v.Cvx, StringComparer.Ordinal)
            .ToArray();
}
