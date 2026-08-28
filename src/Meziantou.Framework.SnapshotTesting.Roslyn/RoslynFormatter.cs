using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.SnapshotTesting.Roslyn;

internal static class RoslynFormatter
{
    /// <summary>
    /// Formats a diagnostic the way the compiler reports it, but with an invariant culture:
    /// <c>Sample.cs(4,10): warning SG0001: message</c>. Roslyn's own <see cref="Diagnostic.ToString"/>
    /// formats the message with the current UI culture, which makes the snapshot depend on the machine.
    /// </summary>
    public static void AppendDiagnostic(StringBuilder builder, Diagnostic diagnostic)
    {
        var location = diagnostic.Location;
        if (location.IsInSource)
        {
            // Positions are zero-based, the compiler reports them one-based.
            var start = location.GetLineSpan().StartLinePosition;
            builder.Append(location.GetLineSpan().Path)
                .Append('(')
                .Append(Format(start.Line + 1))
                .Append(',')
                .Append(Format(start.Character + 1))
                .Append("): ");
        }

        builder.Append(GetSeverity(diagnostic.Severity))
            .Append(' ')
            .Append(diagnostic.Id)
            .Append(": ")
            .Append(diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    public static string FormatDiagnostic(Diagnostic diagnostic)
    {
        var builder = new StringBuilder();
        AppendDiagnostic(builder, diagnostic);
        return builder.ToString();
    }

    /// <summary>
    /// Orders diagnostics so a snapshot does not depend on the order the caller happened to collect them in.
    /// </summary>
    /// <remarks>
    /// The key is culture-independent on purpose. <see cref="Diagnostic.ToString" /> formats the message with
    /// the current UI culture, so using it as a sort key would leave the order itself machine-dependent - the
    /// same reason <see cref="AppendDiagnostic" /> does not use it either.
    /// </remarks>
    public static IComparer<Diagnostic> DiagnosticComparer { get; } = new OrdinalDiagnosticComparer();

    private sealed class OrdinalDiagnosticComparer : IComparer<Diagnostic>
    {
        public int Compare(Diagnostic? x, Diagnostic? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x is null)
                return -1;

            if (y is null)
                return 1;

            // Position first, so the report reads in the order a compiler would emit it.
            var xSpan = x.Location.GetLineSpan();
            var ySpan = y.Location.GetLineSpan();

            var result = StringComparer.Ordinal.Compare(xSpan.Path, ySpan.Path);
            if (result != 0)
                return result;

            result = xSpan.StartLinePosition.CompareTo(ySpan.StartLinePosition);
            if (result != 0)
                return result;

            result = StringComparer.Ordinal.Compare(x.Id, y.Id);
            if (result != 0)
                return result;

            return StringComparer.Ordinal.Compare(
                x.GetMessage(CultureInfo.InvariantCulture),
                y.GetMessage(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Formats a position as <c>line,character</c>, keeping the zero-based values Roslyn exposes.</summary>
    public static string FormatPosition(LinePosition position) => Format(position.Line) + "," + Format(position.Character);

    public static string FormatSpan(LinePositionSpan span) => "(" + FormatPosition(span.Start) + ")-(" + FormatPosition(span.End) + ")";

    public static string FormatSpan(TextSpan span) => "[" + Format(span.Start) + ".." + Format(span.End) + ")";

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string GetSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Hidden => "hidden",
        DiagnosticSeverity.Info => "info",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Error => "error",
        _ => severity.ToString(),
    };
}
