/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Common;

namespace OpenCdsi.VaxEngine.Core.ReferenceData;

/// <summary>
/// §7.3 Determine Contraindications. The &lt;contraindications&gt; element is a sibling of
/// &lt;series&gt;/&lt;immunity&gt; at the antigen file root - one per antigen.
///
/// DATA LIMITATION, flagged: Table 7-4 lists "Active Patient Observations" and "Adverse
/// Reactions" as two separate patient-side attributes, and Tables 7-5/7-6 treat "does this
/// contraindication describe an observation" and "...an adverse reaction" as two distinct
/// conditions. But the real XML only has ONE observationCode field per contraindication entry -
/// there is no structural discriminator anywhere in this dataset (checked the Schedule file's
/// own `observations` catalog too; its `group` field is empty on all 277 entries). Real codes
/// clearly span both concepts (e.g. "007"/"Pregnant" is an observation; "091"/"Severe allergic
/// reaction after previous dose of Measles" is an adverse reaction) with no field marking which
/// is which. This codebase checks a contraindication's ObservationCode against BOTH
/// Patient.ActiveObservations and Patient.AdverseReactions, treating a match in either as
/// satisfying the "describes an observation/adverse reaction" condition - a documented
/// simplification of the two-condition table, not a guess at hidden structure that isn't there.
/// </summary>
public sealed class AntigenContraindicationData
{
    public required IReadOnlyList<AntigenContraindication> AntigenLevel { get; init; }
    public required IReadOnlyList<VaccineContraindication> VaccineLevel { get; init; }
}

/// <summary>Table 7-5. Applying this prevents ALL relevant patient series for the antigen from being forecast.</summary>
public sealed class AntigenContraindication
{
    public required string ObservationCode { get; init; }
    public string? ObservationTitle { get; init; }
    public string? ContraindicationText { get; init; }

    /// <summary>§7.5 FORECASTGUIDANCE-1. Real data: 16 of 392 total (antigen + vaccine level combined) have non-empty guidance.</summary>
    public string? ContraindicationGuidance { get; init; }

    public DurationExpression? BeginAge { get; init; }
    public DurationExpression? EndAge { get; init; }

    private static readonly DateOnly DefaultFloor = new(1900, 1, 1);
    private static readonly DateOnly DefaultCeiling = new(2999, 12, 31);

    public DateOnly BeginAgeDate(DateOnly dob) => BeginAge?.AddTo(dob) ?? DefaultFloor;
    public DateOnly EndAgeDate(DateOnly dob) => EndAge?.AddTo(dob) ?? DefaultCeiling;
}

/// <summary>Table 7-6. Applying this eliminates specific contraindicated vaccine types from being forecast, not the whole antigen.</summary>
public sealed class VaccineContraindication
{
    public required string ObservationCode { get; init; }
    public string? ObservationTitle { get; init; }
    public string? ContraindicationText { get; init; }
    public string? ContraindicationGuidance { get; init; }
    public required IReadOnlyList<ContraindicatedVaccine> ContraindicatedVaccines { get; init; }
}

/// <summary>One vaccine type this contraindication rules out, with its own age window (per-vaccine, not shared with the parent VaccineContraindication).</summary>
public sealed class ContraindicatedVaccine
{
    public required string Cvx { get; init; }
    public DurationExpression? BeginAge { get; init; }
    public DurationExpression? EndAge { get; init; }

    private static readonly DateOnly DefaultFloor = new(1900, 1, 1);
    private static readonly DateOnly DefaultCeiling = new(2999, 12, 31);

    public DateOnly BeginAgeDate(DateOnly dob) => BeginAge?.AddTo(dob) ?? DefaultFloor;
    public DateOnly EndAgeDate(DateOnly dob) => EndAge?.AddTo(dob) ?? DefaultCeiling;
}
