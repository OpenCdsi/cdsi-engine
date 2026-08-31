/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Contracts.ReferenceData;

public sealed class VaccineSummaryDto
{
    public required string Cvx { get; init; }
    public string? ShortDescription { get; init; }
}

public sealed class VaccineDto
{
    public required string Cvx { get; init; }
    public string? ShortDescription { get; init; }
    public required IReadOnlyList<CvxAssociationDto> Associations { get; init; }
}

public sealed class CvxAssociationDto
{
    public required string Antigen { get; init; }
    public string? AssociationBeginAge { get; init; }
    public string? AssociationEndAge { get; init; }
}

public sealed class VaccineConflictDto
{
    public required string ConflictingVaccineType { get; init; }
    public required string ConflictingCvx { get; init; }
    public required string ImpactedVaccineType { get; init; }
    public required string ImpactedCvx { get; init; }
    public required string ConflictBeginInterval { get; init; }
    public required string MinConflictEndInterval { get; init; }
    public required string ConflictEndInterval { get; init; }
}
