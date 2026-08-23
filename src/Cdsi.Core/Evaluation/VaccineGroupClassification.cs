namespace Cdsi.Core.Evaluation;

/// <summary>§9.1: is a vaccine group single-antigen or multiple-antigen (VACCINEGROUP-1/2)?</summary>
public enum VaccineGroupType { SingleAntigen, MultipleAntigen }

public static class VaccineGroupClassification
{
    /// <summary>VACCINEGROUP-1/2. Takes the antigen names a group classifies (derive this by grouping AntigenSeries by their own VaccineGroup field across all loaded antigen files - see VaccineGroupInfo's doc comment for why the Schedule file's own vaccineGroupToAntigenMap table isn't a reliable source for this).</summary>
    public static VaccineGroupType Classify(IReadOnlyList<string> antigensInGroup) =>
        antigensInGroup.Distinct().Count() == 1 ? VaccineGroupType.SingleAntigen : VaccineGroupType.MultipleAntigen;
}
