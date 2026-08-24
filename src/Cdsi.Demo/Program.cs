/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Cdsi.Core.ReferenceData;

// Loads the FULL real CDC catalog (all 30 antigens + schedule) and runs a few sample patients
// through GeneratePatientForecast end to end - the whole pipeline, real data, nothing mocked.
// §6.2's "Completed Series" condition is resolved internally via two evaluation passes -
// GeneratePatientForecast handles this on its own, no caller-supplied resolver needed anymore.

var dataRoot = FindDataDirectory();
Console.WriteLine($"Loading full CDC catalog from: {dataRoot}");
var repo = ReferenceDataRepository.Load(Path.Combine(dataRoot, "antigens"), Path.Combine(dataRoot, "schedule", "ScheduleSupportingData.xml"));
Console.WriteLine($"Loaded {repo.AllSeries.Count} series across {repo.AllSeries.Select(s => s.Antigen).Distinct().Count()} antigens, {repo.VaccineGroups.Count} vaccine groups.");
Console.WriteLine();

// Chosen deliberately, not arbitrarily: real on-file seasonal windows for RSV (2025-10-01 to
// 2026-03-31) and Influenza (2025-07-01 to 2026-06-30) overlap here. An earlier run with an
// August assessment date correctly, but confusingly, showed both as "NotRecommended" (off
// season per real seasonal data - not a bug, see README) - mid-January avoids that so the demo's
// own output is easy to read at a glance instead of needing a season-staleness footnote.
var today = new DateOnly(2026, 1, 15);

RunPatient("Newborn, no doses yet", today, Array.Empty<VaccineDoseAdministered>());

RunPatient("2-month-old, birth-dose HepB only", today.AddMonths(-2), new[]
{
    new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = today.AddMonths(-2) }
});

var fifteenMonthOldDob = today.AddMonths(-15);
RunPatient("15-month-old, partway through routine schedule", fifteenMonthOldDob, new[]
{
    new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = fifteenMonthOldDob },                    // HepB birth dose
    new VaccineDoseAdministered { DoseId = "d2", Cvx = "08", DateAdministered = fifteenMonthOldDob.AddMonths(2) },       // HepB dose 2
    new VaccineDoseAdministered { DoseId = "d3", Cvx = "110", DateAdministered = fifteenMonthOldDob.AddMonths(2) },      // DTaP-HepB-IPV dose 1
    new VaccineDoseAdministered { DoseId = "d4", Cvx = "110", DateAdministered = fifteenMonthOldDob.AddMonths(4) },      // DTaP-HepB-IPV dose 2
});

void RunPatient(string label, DateOnly dob, IReadOnlyList<VaccineDoseAdministered> doses)
{
    Console.WriteLine($"=== {label} (DOB {dob:yyyy-MM-dd}, assessed {today:yyyy-MM-dd}) ===");
    Console.WriteLine($"Doses administered: {doses.Count}");

    var patient = new Patient { PatientId = label, DateOfBirth = dob };

    var results = GeneratePatientForecast.Execute(
        patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
        repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, today);

    Console.WriteLine($"Vaccine group forecasts produced: {results.Count}");
    Console.WriteLine();

    foreach (var vg in results.OrderBy(r => r.VaccineGroupName))
    {
        Console.WriteLine($"  {vg.VaccineGroupName} ({vg.Type})");
        Console.WriteLine($"    Status: {vg.Status}   ShouldForecast: {vg.ShouldForecast}");
        if (vg.ShouldForecast)
        {
            Console.WriteLine($"    Dose #{vg.ForecastDoseNumber}: earliest {vg.EarliestDate:yyyy-MM-dd} | recommended {vg.AdjustedRecommendedDate:yyyy-MM-dd} | past due {vg.AdjustedPastDueDate:yyyy-MM-dd} | latest {vg.LatestDate:yyyy-MM-dd}");
            if (vg.RecommendedVaccineCvxCodes.Count > 0)
            {
                Console.WriteLine($"    Recommended CVX codes: {string.Join(", ", vg.RecommendedVaccineCvxCodes)}");
            }
            if (vg.AllPreferableVaccineCvxCodes.Count > 0)
            {
                Console.WriteLine($"    All clinically valid CVX codes: {string.Join(", ", vg.AllPreferableVaccineCvxCodes)}");
            }
        }
        Console.WriteLine();
    }

    Console.WriteLine();
}

static string FindDataDirectory()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cdsi.sln")))
    {
        dir = dir.Parent;
    }
    if (dir is null)
    {
        throw new InvalidOperationException("Couldn't find the repo root (Cdsi.sln) walking up from the executable's directory - run this from within the cdsi-engine checkout.");
    }
    return Path.Combine(dir.FullName, "data");
}
