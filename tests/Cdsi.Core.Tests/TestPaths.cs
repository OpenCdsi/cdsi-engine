/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Tests;

internal static class TestPaths
{
    public static string AntigensDirectory => Path.Combine(AppContext.BaseDirectory, "TestData", "antigens");
    public static string ScheduleFilePath => Path.Combine(AppContext.BaseDirectory, "TestData", "schedule", "ScheduleSupportingData.xml");

    /// <summary>
    /// Resolves an antigen's real data file by SEARCHING the antigens directory for a filename
    /// that matches <paramref name="antigenName"/>, rather than requiring an exact filename -
    /// found necessary because the real, current filenames (AntigenSupportingData-_Pertussis-
    /// 508.xml and so on) have underscores standing in for what the CDC's own delivered files use
    /// spaces for, and a future CDC data refresh could plausibly restore the original spacing, or
    /// bump the trailing revision number (currently 508). Matching was previously a plain,
    /// hardcoded Path.Combine to the exact current filename in every one of dozens of call sites
    /// across this project's tests - a real data refresh that changed spacing, casing, or the
    /// revision suffix would have broken every one of them with a bare FileNotFoundException,
    /// with no single place to fix it.
    ///
    /// Matching is done by normalizing both the search term and each real filename (keeping only
    /// letters and digits, uppercased) and checking for a substring match - so "Meningococcal B"
    /// (the argument callers should pass, matching the antigen's own real name, not the filename)
    /// matches the real file's "Meningococcal_B" as easily as it would match a hypothetical CDC
    /// original "Meningococcal B", regardless of spacing, underscores, or hyphens in between.
    /// Throws a clear, specific error on zero or multiple matches (rather than silently picking
    /// one or returning a non-existent path) precisely because ambiguity here is a real signal
    /// worth surfacing loudly, not something to guess through.
    /// </summary>
    public static string AntigenFile(string antigenName)
    {
        var normalizedSearch = Normalize(antigenName);
        var candidates = Directory.EnumerateFiles(AntigensDirectory, "AntigenSupportingData-*.xml")
            .Where(f => Normalize(Path.GetFileNameWithoutExtension(f)).Contains(normalizedSearch))
            .ToArray();

        if (candidates.Length == 0)
        {
            var allFiles = string.Join(", ", Directory.EnumerateFiles(AntigensDirectory, "AntigenSupportingData-*.xml").Select(Path.GetFileName));
            throw new FileNotFoundException(
                $"No antigen data file found matching '{antigenName}' in {AntigensDirectory}. Files present: {allFiles}");
        }
        if (candidates.Length > 1)
        {
            throw new InvalidOperationException(
                $"'{antigenName}' matched {candidates.Length} antigen data files, expected exactly one: {string.Join(", ", candidates.Select(Path.GetFileName))}");
        }
        return candidates[0];
    }

    private static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).ToArray().Select(char.ToUpperInvariant).ToArray());
}
