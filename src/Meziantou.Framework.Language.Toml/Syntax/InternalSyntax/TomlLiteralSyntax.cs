using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>A value written as a single token: a string, a number, a boolean, a date, or a time.</summary>
/// <remarks>
/// The kind comes from the token, so every scalar shares this one green type; the red type it projects to is the
/// one that gives the token its typed value.
/// </remarks>
internal sealed class TomlLiteralSyntax : TomlValueSyntax
{
    private readonly GreenNode _token;

    public TomlLiteralSyntax(SyntaxKind kind, GreenNode token)
        : this(kind, token, diagnostics: null, annotations: null)
    {
    }

    private TomlLiteralSyntax(SyntaxKind kind, GreenNode token, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(token);
        _token = token;
    }

    internal override GreenNode? GetSlot(int index) => index == 0 ? _token : null;
    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlLiteralSyntax(Kind, RequiredSlot(slots[0]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlLiteralSyntax(Kind, _token, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlLiteralSyntax(Kind, _token, GetDiagnostics(), annotations);

    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => Kind switch
    {
        SyntaxKind.TomlString => new Toml.TomlStringSyntax(this, parent, position),
        SyntaxKind.TomlInteger => new Toml.TomlIntegerSyntax(this, parent, position),
        SyntaxKind.TomlFloat => new Toml.TomlFloatSyntax(this, parent, position),
        SyntaxKind.TomlBoolean => new Toml.TomlBooleanSyntax(this, parent, position),
        _ => new Toml.TomlDateTimeSyntax(this, parent, position),
    };
}
