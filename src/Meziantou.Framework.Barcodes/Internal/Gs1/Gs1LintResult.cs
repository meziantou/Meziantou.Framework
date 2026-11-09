using System.Runtime.InteropServices;

namespace Meziantou.Framework.Barcodes.Internal.Gs1;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct Gs1LintResult(Gs1LintError Error, int Position, int Length)
{
    public static Gs1LintResult Ok => default;

    public bool IsValid => Error is Gs1LintError.None;
}
