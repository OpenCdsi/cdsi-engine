using System.Xml.Linq;

namespace Cdsi.Core.ReferenceData;

public sealed class ScheduleSupportingData
{
    public required IReadOnlyDictionary<string, CvxMapEntry> CvxToAntigen { get; init; }
    public required IReadOnlyList<VaccineConflictRule> VaccineConflicts { get; init; }
}

/// <summary>Loads ScheduleSupportingData.xml — the cross-antigen lookups (CVX-to-antigen map, vaccine conflicts) that §4.2 and §6.7 depend on.</summary>
public static class ScheduleSupportingDataLoader
{
    public static ScheduleSupportingData LoadFile(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new InvalidOperationException($"'{path}' has no root element.");

        var cvxMapRoot = root.Element("cvxToAntigenMap") ?? root;
        var cvxEntries = cvxMapRoot.Elements("cvxMap")
            .Select(ParseCvxMapEntry)
            .ToDictionary(e => e.Cvx, e => e);

        var conflictsRoot = root.Element("liveVirusConflicts") ?? root;
        var conflicts = conflictsRoot.Elements("liveVirusConflict")
            .Select(ParseConflictRule)
            .ToArray();

        return new ScheduleSupportingData
        {
            CvxToAntigen = cvxEntries,
            VaccineConflicts = conflicts
        };
    }

    private static CvxMapEntry ParseCvxMapEntry(XElement el)
    {
        var cvx = el.ElementTextOrNull("cvx") ?? throw new InvalidOperationException("cvxMap entry missing cvx.");
        var associations = el.Elements("association").Select(a => new CvxAssociation
        {
            Antigen = a.ElementTextOrNull("antigen") ?? throw new InvalidOperationException($"association under cvx '{cvx}' missing antigen."),
            AssociationBeginAge = a.ParseDurationOrNull("associationBeginAge"),
            AssociationEndAge = a.ParseDurationOrNull("associationEndAge")
        }).ToArray();

        return new CvxMapEntry
        {
            Cvx = cvx,
            ShortDescription = el.ElementTextOrNull("shortDescription"),
            Associations = associations
        };
    }

    private static VaccineConflictRule ParseConflictRule(XElement el)
    {
        var previous = el.Element("previous") ?? throw new InvalidOperationException("liveVirusConflict missing <previous>.");
        var current = el.Element("current") ?? throw new InvalidOperationException("liveVirusConflict missing <current>.");

        return new VaccineConflictRule
        {
            ConflictingVaccineType = previous.ElementTextOrNull("vaccineType") ?? "",
            ConflictingCvx = previous.ElementTextOrNull("cvx") ?? "",
            ImpactedVaccineType = current.ElementTextOrNull("vaccineType") ?? "",
            ImpactedCvx = current.ElementTextOrNull("cvx") ?? "",
            ConflictBeginInterval = el.ParseDurationOrNull("conflictBeginInterval")
                ?? throw new InvalidOperationException("liveVirusConflict missing conflictBeginInterval."),
            MinConflictEndInterval = el.ParseDurationOrNull("minConflictEndInterval")
                ?? throw new InvalidOperationException("liveVirusConflict missing minConflictEndInterval."),
            ConflictEndInterval = el.ParseDurationOrNull("conflictEndInterval")
                ?? throw new InvalidOperationException("liveVirusConflict missing conflictEndInterval.")
        };
    }
}
