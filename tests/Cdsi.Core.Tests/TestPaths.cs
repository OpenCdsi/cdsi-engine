namespace Cdsi.Core.Tests;

internal static class TestPaths
{
    public static string AntigensDirectory => Path.Combine(AppContext.BaseDirectory, "TestData", "antigens");
    public static string ScheduleFilePath => Path.Combine(AppContext.BaseDirectory, "TestData", "schedule", "ScheduleSupportingData.xml");
    public static string AntigenFile(string name) => Path.Combine(AntigensDirectory, name);
}
