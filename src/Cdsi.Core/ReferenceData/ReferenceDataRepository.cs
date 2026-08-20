namespace Cdsi.Core.ReferenceData;

/// <summary>Convenience loader: reads every AntigenSupportingData-*.xml in a directory plus the Schedule file, and holds the combined in-memory catalog. This is the object your API's startup/DI wiring should build once (per your "easy updates" priority: rebuilding this from a mounted data volume, not a code change, is how a new CDC data drop gets picked up).</summary>
public sealed class ReferenceDataRepository
{
    public required IReadOnlyList<AntigenSeries> AllSeries { get; init; }
    public required ScheduleSupportingData Schedule { get; init; }

    public static ReferenceDataRepository Load(string antigensDirectory, string scheduleFilePath)
    {
        var allSeries = new List<AntigenSeries>();
        foreach (var file in Directory.EnumerateFiles(antigensDirectory, "AntigenSupportingData-*.xml").OrderBy(f => f, StringComparer.Ordinal))
        {
            allSeries.AddRange(AntigenSupportingDataLoader.LoadFile(file));
        }

        var schedule = ScheduleSupportingDataLoader.LoadFile(scheduleFilePath);

        return new ReferenceDataRepository
        {
            AllSeries = allSeries,
            Schedule = schedule
        };
    }
}
