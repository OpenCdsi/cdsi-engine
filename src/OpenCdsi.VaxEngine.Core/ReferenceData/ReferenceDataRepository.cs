/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Core.ReferenceData;

/// <summary>Convenience loader: reads every AntigenSupportingData-*.xml in a directory plus the Schedule file, and holds the combined in-memory catalog. This is the object your API's startup/DI wiring should build once (per your "easy updates" priority: rebuilding this from a mounted data volume, not a code change, is how a new CDC data drop gets picked up).</summary>
public sealed class ReferenceDataRepository
{
    public required IReadOnlyList<AntigenSeries> AllSeries { get; init; }
    public required ScheduleSupportingData Schedule { get; init; }

    /// <summary>Keyed by antigen name (matches AntigenSeries.Antigen) - everything GeneratePatientForecast needs to actually run the full pipeline, not just enumerate series.</summary>
    public required IReadOnlyDictionary<string, AntigenImmunityData> ImmunityByAntigen { get; init; }
    public required IReadOnlyDictionary<string, AntigenContraindicationData> ContraindicationsByAntigen { get; init; }
    public required IReadOnlyList<VaccineGroupInfo> VaccineGroups { get; init; }

    public static ReferenceDataRepository Load(string antigensDirectory, string scheduleFilePath)
    {
        var allSeries = new List<AntigenSeries>();
        var immunityByAntigen = new Dictionary<string, AntigenImmunityData>();
        var contraindicationsByAntigen = new Dictionary<string, AntigenContraindicationData>();

        foreach (var file in Directory.EnumerateFiles(antigensDirectory, "AntigenSupportingData-*.xml").OrderBy(f => f, StringComparer.Ordinal))
        {
            var series = AntigenSupportingDataLoader.LoadFile(file);
            allSeries.AddRange(series);

            var immunity = AntigenSupportingDataLoader.LoadImmunityData(file);
            var contraindications = AntigenSupportingDataLoader.LoadContraindicationData(file);

            // Every real file defines exactly one antigen in practice, but key by whatever
            // distinct antigen names actually appear rather than assuming it, in case that
            // ever isn't true for some future data drop.
            foreach (var antigenName in series.Select(s => s.Antigen).Distinct())
            {
                immunityByAntigen[antigenName] = immunity;
                contraindicationsByAntigen[antigenName] = contraindications;
            }
        }

        var schedule = ScheduleSupportingDataLoader.LoadFile(scheduleFilePath);
        var vaccineGroups = ScheduleSupportingDataLoader.LoadVaccineGroups(scheduleFilePath);

        return new ReferenceDataRepository
        {
            AllSeries = allSeries,
            Schedule = schedule,
            ImmunityByAntigen = immunityByAntigen,
            ContraindicationsByAntigen = contraindicationsByAntigen,
            VaccineGroups = vaccineGroups
        };
    }
}
