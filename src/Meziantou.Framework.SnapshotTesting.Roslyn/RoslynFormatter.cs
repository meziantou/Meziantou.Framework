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
