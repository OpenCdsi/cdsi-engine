/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Core.ReferenceData;

/// <summary>§7.2 Determine Evidence of Immunity (Table 7-2/7-3). The &lt;immunity&gt; element is a sibling of &lt;series&gt; at the root of an antigen supporting-data file - one per antigen, not per series/dose.</summary>
public sealed class AntigenImmunityData
{
    public required IReadOnlyList<ImmunityClinicalHistoryGuideline> ClinicalHistoryGuidelines { get; init; }
    public required IReadOnlyList<ImmunityBirthDateRule> BirthDateRules { get; init; }
}

/// <summary>A documented clinical finding (e.g. a positive titer, a prior diagnosis) that, if present in the patient's history, is sufficient evidence of immunity on its own (Table 7-3 Rule 1).</summary>
public sealed class ImmunityClinicalHistoryGuideline
{
    public required string GuidelineCode { get; init; }
    public string? GuidelineTitle { get; init; }
}

/// <summary>The "born before a defined date implies presumed immunity" rule (e.g. Measles: born before 01/01/1957). Note the date format here is MM/DD/YYYY, NOT the yyyyMMdd format used everywhere else in this dataset - confirmed against all 4 real instances before parsing this way.</summary>
public sealed class ImmunityBirthDateRule
{
    public required DateOnly ImmunityBirthDate { get; init; }
    public string? BirthCountry { get; init; }
    public required IReadOnlyList<ImmunityExclusion> Exclusions { get; init; }
}

/// <summary>A condition that overrides the birth-date presumption (e.g. "Health care personnel" - occupational exposure risk means the birth-year presumption shouldn't apply, per the spec's own example).</summary>
public sealed class ImmunityExclusion
{
    public required string ExclusionCode { get; init; }
    public string? ExclusionTitle { get; init; }
}
