using Meziantou.Framework.InlineSnapshotTesting;

namespace HttpCaching.Tests.Internals;

internal static class Extensions
{
    public static void ScrubHeaders(this InlineSnapshotSettings settings, params string[] headerNames)
    {
        settings.ScrubLinesWithReplace(line =>
        {
            foreach (var headerName in headerNames)
            {
                if (line.AsSpan().TrimStart().StartsWith(headerName + ":", StringComparison.OrdinalIgnoreCase))
                {
                    var spaces = CountLeadingSpaces(line);
                    return $"{new string(' ', spaces)}{headerName}: <redacted>";
                }
            }
            return line;
        });
    }

    private static int CountLeadingSpaces(string line)
    {
        return line.TakeWhile(c => c == ' ').Count();
    }
}