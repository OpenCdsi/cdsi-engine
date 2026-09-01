/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Contracts.ReferenceData;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Api;

/// <summary>
/// GET /api/v3/vaccines/groups/* - antigen membership is deliberately derived from
/// AntigenSeries.VaccineGroup across all antigen files, NOT from the Schedule file's own
/// vaccineGroupToAntigenMap element - see VaccineGroupDto's own doc comment for why (that table
/// is documented as incomplete for real multi-antigen groups, dropping antigens like Mumps/
/// Rubella from MMR).
/// </summary>
public static class VaccineGroupEndpoints
{
    public static void MapVaccineGroupEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/vaccines/groups", (ReferenceDataRepository data) => Results.Ok(GetSummaries(data)))
            .WithName("GetVaccineGroups")
            .WithTags("Supporting Data");

        group.MapGet("/vaccines/groups/{name}", (string name, ReferenceDataRepository data) =>
        {
            var vaccineGroup = FindGroup(data, name);
            return vaccineGroup is not null
                ? Results.Ok(ReferenceDataMapping.ToDto(vaccineGroup, AntigensInGroup(data, vaccineGroup.Name)))
                : Results.NotFound();
        })
            .WithName("GetVaccineGroupByName")
            .WithTags("Supporting Data");

        group.MapGet("/vaccines/groups/{name}/antigens", (string name, ReferenceDataRepository data) =>
        {
            var vaccineGroup = FindGroup(data, name);
            return vaccineGroup is not null
                ? Results.Ok(AntigensInGroup(data, vaccineGroup.Name).ToArray())
                : Results.NotFound();
        })
            .WithName("GetVaccineGroupAntigens")
            .WithTags("Supporting Data");
    }

    private static IReadOnlyList<VaccineGroupSummaryDto> GetSummaries(ReferenceDataRepository data) =>
        data.VaccineGroups
            .Select(ReferenceDataMapping.ToSummaryDto)
            .OrderBy(g => g.Name, StringComparer.Ordinal)
            .ToArray();

    private static VaccineGroupInfo? FindGroup(ReferenceDataRepository data, string name) =>
        data.VaccineGroups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Matched against the group's own real Name (not the caller's possibly-different-case input) - AntigenSeries.VaccineGroup values are exact, real data strings, so the comparison here is ordinal on purpose.</summary>
    private static IEnumerable<string> AntigensInGroup(ReferenceDataRepository data, string groupName) =>
        data.AllSeries
            .Where(s => s.VaccineGroup == groupName)
            .Select(s => s.Antigen)
            .Distinct()
            .OrderBy(a => a, StringComparer.Ordinal);
}
