/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Contracts.ReferenceData;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Api;

/// <summary>
/// GET /api/v2/vaccines/groups/* - antigen membership is deliberately derived from
/// AntigenSeries.VaccineGroup across all antigen files, NOT from the Schedule file's own
/// vaccineGroupToAntigenMap element - see VaccineGroupDto's own doc comment for why (that table
/// is documented as incomplete for real multi-antigen groups, dropping antigens like Mumps/
/// Rubella from MMR).
/// </summary>
public static class VaccineGroupEndpoints
{
    public static void MapVaccineGroupEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v2/vaccines/groups/catalog", (ReferenceDataRepository data) => Results.Ok(GetSummaries(data)))
            .WithName("GetVaccineGroupCatalog")
            .WithTags("VaccineGroups");

        app.MapGet("/api/v2/vaccines/groups", (ReferenceDataRepository data) => Results.Ok(GetSummaries(data)))
            .WithName("GetVaccineGroups")
            .WithTags("VaccineGroups");

        app.MapGet("/api/v2/vaccines/groups/{name}", (string name, ReferenceDataRepository data) =>
        {
            var group = FindGroup(data, name);
            return group is not null
                ? Results.Ok(ReferenceDataMapping.ToDto(group, AntigensInGroup(data, group.Name)))
                : Results.NotFound();
        })
            .WithName("GetVaccineGroupByName")
            .WithTags("VaccineGroups");

        app.MapGet("/api/v2/vaccines/groups/{name}/antigens", (string name, ReferenceDataRepository data) =>
        {
            var group = FindGroup(data, name);
            return group is not null
                ? Results.Ok(AntigensInGroup(data, group.Name).ToArray())
                : Results.NotFound();
        })
            .WithName("GetVaccineGroupAntigens")
            .WithTags("VaccineGroups");
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
