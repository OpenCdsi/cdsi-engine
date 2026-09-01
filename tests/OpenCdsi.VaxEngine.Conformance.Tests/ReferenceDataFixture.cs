/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Conformance.Tests;

/// <summary>
/// Loads the real, full 30-antigen catalog exactly ONCE for the whole 1,064-case run, via
/// xUnit's IClassFixture - without this, xUnit's default "new test class instance per [Theory]
/// case" behavior would reload the entire XML catalog 1,064 times, which would be needlessly
/// slow (and pointless, since the catalog never changes between cases).
/// </summary>
public sealed class ReferenceDataFixture
{
    public ReferenceDataRepository Repository { get; }

    public ReferenceDataFixture()
    {
        var antigensPath = Path.Combine(AppContext.BaseDirectory, "TestData", "antigens");
        var schedulePath = Path.Combine(AppContext.BaseDirectory, "TestData", "schedule", "ScheduleSupportingData.xml");
        Repository = ReferenceDataRepository.Load(antigensPath, schedulePath);
    }
}
