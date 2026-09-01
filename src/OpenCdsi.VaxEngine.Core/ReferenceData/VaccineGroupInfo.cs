/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Core.ReferenceData;

/// <summary>
/// §9.1's &lt;vaccineGroups&gt; element from the Schedule file. Real data: only 2 of 26 groups
/// specify AdministerFullVaccineGroup at all (MMR = "Yes", DTaP/Tdap/Td = "No") - the other 24
/// are single-antigen groups where the flag is irrelevant (FORECASTDN-2's MIN/MAX choice only
/// matters when a group's forecast could be built from more than one contained forecast).
///
/// NOTE: antigen membership is NOT derived from this file's &lt;vaccineGroupToAntigenMap&gt;
/// element - that table is incomplete for real multi-antigen groups (it lists only one antigen
/// per group name, e.g. "MMR" -> "Measles" alone, dropping Mumps/Rubella). The complete,
/// verified-consistent source is each antigen file's OWN &lt;series&gt;&lt;vaccineGroup&gt;
/// field (already parsed into AntigenSeries.VaccineGroup) - grouping antigens by that value
/// across all 30 files correctly recovers MMR = {Measles, Mumps, Rubella} and
/// DTaP/Tdap/Td = {Diphtheria, Tetanus, Pertussis}, confirmed against real data before building
/// anything on top of the Schedule table instead.
/// </summary>
public sealed class VaccineGroupInfo
{
    public required string Name { get; init; }
    public bool? AdministerFullVaccineGroup { get; init; }
}
