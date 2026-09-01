/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Core.Common;

/// <summary>
/// Implemented by any supporting-data element that can carry multiple time-boxed instances
/// (Logic Spec §3.3 "Selecting Supporting Data" — applies to Age, Preferable Interval,
/// Allowable Interval, and Conditional Skip attributes, and by the same pattern, the
/// CVX-to-antigen association ages in the Schedule supporting data).
/// </summary>
public interface ITemporallyVersioned
{
    DateOnly? EffectiveDate { get; }
    DateOnly? CessationDate { get; }
}
