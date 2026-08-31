/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Contracts.ReferenceData;

public sealed class VaccineGroupSummaryDto
{
    public required string Name { get; init; }
}

/// <summary>
/// Antigens is deliberately NOT derived from the Schedule file's own vaccineGroupToAntigenMap
/// element - that table is documented as incomplete for real multi-antigen groups (see
/// VaccineGroupInfo's own doc comment: it drops Mumps/Rubella from MMR, keeping only Measles).
/// The complete, verified source is grouping AntigenSeries.VaccineGroup across all antigen
/// files - see ReferenceDataMapping.ToVaccineGroupDto, which does exactly what
/// VaccineGroupInfo's own comment says is the only correct way to recover this.
/// </summary>
public sealed class VaccineGroupDto
{
    public required string Name { get; init; }
    public bool? AdministerFullVaccineGroup { get; init; }
    public required IReadOnlyList<string> Antigens { get; init; }
}
