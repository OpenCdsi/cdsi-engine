/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.ReferenceData;
using Xunit;

namespace OpenCdsi.VaxEngine.Core.Tests;

public class VaccineGroupClassificationTests
{
    // Loads all 30 real antigen files to derive the actual antigen-to-vaccine-group mapping,
    // grouping by each antigen's own VaccineGroup field (see VaccineGroupInfo's doc comment for
    // why the Schedule file's vaccineGroupToAntigenMap table isn't used for this).
    private static readonly IReadOnlyList<AntigenSeries> AllSeries =
        ReferenceDataRepository.Load(TestPaths.AntigensDirectory, TestPaths.ScheduleFilePath).AllSeries;

    private static IReadOnlyList<string> AntigensInGroup(string groupName) =>
        AllSeries.Where(s => s.VaccineGroup == groupName).Select(s => s.Antigen).Distinct().ToArray();

    [Fact]
    public void MMR_RealData_ClassifiesAsMultipleAntigen()
    {
        var antigens = AntigensInGroup("MMR");

        Assert.Equal(new[] { "Measles", "Mumps", "Rubella" }, antigens.OrderBy(a => a));
        Assert.Equal(VaccineGroupType.MultipleAntigen, VaccineGroupClassification.Classify(antigens));
    }

    [Fact]
    public void DTaPTdapTd_RealData_ClassifiesAsMultipleAntigen()
    {
        var antigens = AntigensInGroup("DTaP/Tdap/Td");

        Assert.Equal(new[] { "Diphtheria", "Pertussis", "Tetanus" }, antigens.OrderBy(a => a));
        Assert.Equal(VaccineGroupType.MultipleAntigen, VaccineGroupClassification.Classify(antigens));
    }

    [Fact]
    public void HepB_RealData_ClassifiesAsSingleAntigen()
    {
        var antigens = AntigensInGroup("HepB");

        Assert.Equal(new[] { "HepB" }, antigens);
        Assert.Equal(VaccineGroupType.SingleAntigen, VaccineGroupClassification.Classify(antigens));
    }

    [Fact]
    public void EveryOtherRealVaccineGroup_ClassifiesAsSingleAntigen()
    {
        // Confirms MMR and DTaP/Tdap/Td are the ONLY multi-antigen groups in the real dataset.
        var allGroupNames = AllSeries.Select(s => s.VaccineGroup).Distinct().ToArray();
        var multiAntigenGroups = allGroupNames
            .Where(g => g is not null)
            .Where(g => VaccineGroupClassification.Classify(AntigensInGroup(g!)) == VaccineGroupType.MultipleAntigen)
            .ToArray();

        Assert.Equal(new[] { "DTaP/Tdap/Td", "MMR" }, multiAntigenGroups.OrderBy(g => g));
    }
}
