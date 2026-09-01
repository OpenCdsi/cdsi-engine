/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Core.Models;

/// <summary>
/// The output unit of §4.2 Organize Immunization History: one administered dose,
/// exploded/associated to a single antigen. A single VaccineDoseAdministered (e.g. DTaP)
/// produces multiple AntigenAdministered records (one per associated antigen); a single
/// CVX with age-gated associations (e.g. CVX 121, Zoster live) produces exactly one,
/// chosen by the patient's age at administration.
/// </summary>
public sealed class AntigenAdministered
{
    public required string Antigen { get; init; }
    public required DateOnly DateAdministered { get; init; }
    public required string Cvx { get; init; }
    public required VaccineDoseAdministered SourceDose { get; init; }
}
