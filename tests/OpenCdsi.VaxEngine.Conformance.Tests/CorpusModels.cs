/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Conformance.Tests;

/// <summary>
/// Deserialization models for cdsi-healthy-test-cases.json - an external, 1,064-case conformance
/// corpus, not authored by this project (same provenance question as the CDC XML data itself -
/// see data/NOTICE at the repo root; this corpus's own license/source hasn't been confirmed and
/// is worth resolving before this project claims MPL 2.0 covers it too). Field names use
/// PascalCase here with case-insensitive deserialization, matching the source JSON's camelCase.
/// </summary>
public sealed class ConformanceTestCase
{
    public required string TestId { get; init; }
    public required string TestName { get; init; }
    public required DateOnly AssessmentDate { get; init; }
    public required ConformancePatient Patient { get; init; }

    /// <summary>Corpus short code (e.g. "DTAP", "Td") - see VaccineGroupMapping for the real, verified translation to the engine's own VaccineGroup names.</summary>
    public required string VaccineGroup { get; init; }

    /// <summary>"Not complete" / "Complete" / "Aged out" / "Immune" in real corpus data - see VaccineGroupMapping for the mapping to PatientSeriesStatus.</summary>
    public required string SeriesStatus { get; init; }

    public required IReadOnlyList<ConformanceDose> ImmunizationHistory { get; init; }
    public required ConformanceForecast Forecast { get; init; }
    public ConformanceMeta? Meta { get; init; }

    /// <summary>For readable xUnit output - "2013-0002: DTaP #2 at age 10 weeks-5 days" instead of a generic object dump on failure.</summary>
    public override string ToString() => $"{TestId}: {TestName}";
}

public sealed class ConformancePatient
{
    public required DateOnly Dob { get; init; }

    /// <summary>"F" / "M" in real corpus data - see VaccineGroupMapping for the mapping to Gender.</summary>
    public required string Gender { get; init; }

    /// <summary>Always null across the whole real corpus (confirmed by sweeping all 1,064 cases before writing this) - a "healthy patient" corpus with no risk-based immunity/contraindication scenarios.</summary>
    public string? MedicalHistory { get; init; }
}

public sealed class ConformanceDose
{
    public required string Cvx { get; init; }
    public required DateOnly DateAdministered { get; init; }

    /// <summary>The chronological sequence number of this ADMINISTERED dose (1st, 2nd dose given) - NOT the engine's own TargetDoseNumber (which target dose it was evaluated against). Matching corpus doses to engine DoseEvaluationRecords is done by (Cvx, DateAdministered), not this field - see ConformanceTests' own matching logic.</summary>
    public required int DoseNumber { get; init; }

    /// <summary>CDC category text (e.g. "Age: Too Young") when ExpectedStatus is "Not Valid" - never asserted for exact string equality against the engine's own terser reason text ("Too young"); logged on mismatch instead, per an explicit decision made before writing any test code.</summary>
    public string? ExpectedReason { get; init; }

    /// <summary>"Valid" / "Not Valid" / "Extraneous" in real corpus data (no "SubStandard" cases found) - see VaccineGroupMapping for the mapping to EvaluationStatus.</summary>
    public required string ExpectedStatus { get; init; }

    public string? Mvx { get; init; }
    public string? VaccineName { get; init; }
}

public sealed class ConformanceForecast
{
    /// <summary>A JSON string in the real corpus data ("1", not 1) - confirmed by inspecting the raw file before modeling this as anything numeric.</summary>
    public string? ForecastNumber { get; init; }

    public DateOnly? EarliestDate { get; init; }
    public DateOnly? RecommendedDate { get; init; }
    public DateOnly? PastDueDate { get; init; }
}

public sealed class ConformanceMeta
{
    public string? ChangedInVersion { get; init; }
    public string? DateAdded { get; init; }
    public string? DateUpdated { get; init; }
    public string? EvaluationTestType { get; init; }
    public string? EvaluationTestTypeNormalized { get; init; }
    public string? ForecastTestType { get; init; }
    public string? ForecastTestTypeNormalized { get; init; }
    public string? GeneralDescription { get; init; }
    public string? ReasonForChange { get; init; }
}
