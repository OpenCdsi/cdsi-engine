/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Models;

/// <summary>Per Table 5-2, "Assumed Value if Empty" for patient gender is Unknown — never default to a specific gender.</summary>
public enum Gender
{
    Unknown,
    Male,
    Female
}
