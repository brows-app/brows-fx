using System;
using System.IO;
using System.IO.Enumeration;

namespace Brows.IO.Helpers;

internal static class FileSystemNameMatcher {
    public static bool Matches(string searchPattern, string name, EnumerationOptions options) {
        var ignoreCase = options.MatchCasing switch {
            MatchCasing.CaseSensitive => false,
            MatchCasing.CaseInsensitive => true,
            _ => OperatingSystem.IsWindows()
        };
        return options.MatchType switch {
            MatchType.Simple => FileSystemName.MatchesSimpleExpression(searchPattern, name, ignoreCase),
            MatchType.Win32 => FileSystemName.MatchesWin32Expression(
                FileSystemName.TranslateWin32Expression(searchPattern), name, ignoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.MatchType, null)
        };
    }
}
