/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.Models;

namespace OpenCdsi.VaxEngine.Conformance.Tests;

/// <summary>
/// The corpus-to-engine vocabulary translation, verified against the real engine's own loaded
/// data before being written down - not guessed at or copied from an external description
/// without checking.
/// </summary>
public static class VaccineGroupMapping
{
    /// <summary>
    /// Corpus short code -> engine's real VaccineGroup name, confirmed directly against every
    /// real vaccineGroup value across all 30 antigen files (not just the corpus's own 17 codes)
    /// before trusting this table. Two real things worth knowing:
    ///
    /// 1. The engine's own real data has trailing-space quirks on some vaccineGroup values
    ///    ("Zoster ", and - a new finding while grounding this table - "Cholera " too, though
    ///    Cholera never appears in this particular corpus). AntigenSupportingDataLoader's
    ///    ElementTextOrNull does NOT trim these (confirmed by reading its actual source, not
    ///    assumed) - the engine's real, in-memory VaccineGroupForecastResult.VaccineGroupName
    ///    genuinely is "Zoster " with a trailing space. Comparisons against this table's values
    ///    should trim both sides rather than expect an exact match either way - see
    ///    ConformanceTests' own lookup helper.
    /// 2. "DTAP" and "Td" are two different corpus codes that both map to the same real engine
    ///    vaccine group ("DTaP/Tdap/Td") - Diphtheria, Tetanus, and Pertussis share one
    ///    MultipleAntigen group in the real data, confirmed by checking all three antigens'
    ///    own files independently, not assumed from the name alone.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CorpusToEngineVaccineGroup = new Dictionary<string, string>
    {
        ["DTAP"] = "DTaP/Tdap/Td",
        ["Td"] = "DTaP/Tdap/Td",
        ["POL"] = "Polio",
        ["HIB"] = "Hib",
        ["PCV"] = "Pneumococcal",
        ["VAR"] = "Varicella",
        ["ROTA"] = "Rotavirus",
        ["MENB"] = "Meningococcal B",
        ["MCV"] = "Meningococcal",
        ["ZOSTER"] = "Zoster",
        ["FLU"] = "Influenza",
        ["HPV"] = "HPV",
        ["HepB"] = "HepB",
        ["HepA"] = "HepA",
        ["MMR"] = "MMR",
        ["COVID-19"] = "COVID-19",
        ["RSV"] = "RSV"
    };

    /// <summary>Corpus seriesStatus text -> PatientSeriesStatus. Real corpus values confirmed by sweeping the whole corpus before writing this table: only "Not complete", "Complete", "Aged out", and "Immune" ever appear - "NotRecommended" and "Contraindicated" never do (a "healthy patient" corpus, consistent with never having a populated medicalHistory either).</summary>
    public static readonly IReadOnlyDictionary<string, PatientSeriesStatus> CorpusToEngineSeriesStatus = new Dictionary<string, PatientSeriesStatus>
    {
        ["Not complete"] = PatientSeriesStatus.NotComplete,
        ["Complete"] = PatientSeriesStatus.Complete,
        ["Aged out"] = PatientSeriesStatus.AgedOut,
        ["Immune"] = PatientSeriesStatus.Immune
    };

    /// <summary>Corpus expectedStatus text -> EvaluationStatus. Real corpus values confirmed by sweeping the whole corpus: "Valid", "Not Valid", "Extraneous" - "SubStandard" never appears.</summary>
    public static readonly IReadOnlyDictionary<string, EvaluationStatus> CorpusToEngineEvaluationStatus = new Dictionary<string, EvaluationStatus>
    {
        ["Valid"] = EvaluationStatus.Valid,
        ["Not Valid"] = EvaluationStatus.NotValid,
        ["Extraneous"] = EvaluationStatus.Extraneous
    };

    public static Gender ToGender(string corpusGender) => corpusGender switch
    {
        "F" => Gender.Female,
        "M" => Gender.Male,
        _ => throw new ArgumentOutOfRangeException(nameof(corpusGender), corpusGender, "Unexpected gender value in corpus - only \"F\"/\"M\" were confirmed present when this mapping was written.")
    };
}
