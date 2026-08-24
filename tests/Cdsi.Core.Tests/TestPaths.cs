/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Tests;

internal static class TestPaths
{
    public static string AntigensDirectory => Path.Combine(AppContext.BaseDirectory, "TestData", "antigens");
    public static string ScheduleFilePath => Path.Combine(AppContext.BaseDirectory, "TestData", "schedule", "ScheduleSupportingData.xml");
    public static string AntigenFile(string name) => Path.Combine(AntigensDirectory, name);
}
