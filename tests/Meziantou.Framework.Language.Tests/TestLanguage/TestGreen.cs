using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using GreenTrivia = Meziantou.Framework.Language.InternalSyntax.SyntaxTrivia;

namespace Meziantou.Framework.Language.Tests.TestLanguage;

/// <summary>The immutable nodes of the toy language, and the factory that builds them.</summary>
internal static class TestGreen
{
    public static GreenToken Token(TestSyntaxKind kind, string text, GreenNode? leading = null, GreenNode? trailing = null, bool isMissing = false)
        => new((int)kind, text, leading, trailing, isMissing);

    public static GreenTrivia Trivia(TestSyntaxKind kind, string text) => new((int)kind, text);

    /// <summary>The base of every node of the toy language, supplying what the shared layer asks each language for.</summary>
    public abstract class Node : GreenNode
    {
        protected Node(TestSyntaxKind kind)
            : base((int)kind)
        {
        }

        protected Node(TestSyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
            : base((int)kind, diagnostics, annotations)
        {
        }

        public TestSyntaxKind Kind => (TestSyntaxKind)RawKind;

        public override string KindText => Kind.ToString();

        internal override GreenNode? CreateSeparator() => Token(TestSyntaxKind.CommaToken, ",");
    }

    public sealed class Atom : Node
    {
        private readonly GreenNode _identifierToken;

        public Atom(GreenNode identifierToken)
            : this(identifierToken, diagnostics: null, annotations: null)
        {
        }

        private Atom(GreenNode identifierToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
            : base(TestSyntaxKind.TestAtom, diagnostics, annotations)
        {
            SlotCount = 1;
            AdjustFlagsAndWidth(identifierToken);
            _identifierToken = identifierToken;
        }

        internal override GreenNode? GetSlot(int index) => index == 0 ? _identifierToken : null;

        internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new Atom(slots[0]!, GetDiagnostics(), GetAnnotations());

        internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new Atom(_identifierToken, diagnostics, GetAnnotations());
        internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new Atom(_identifierToken, GetDiagnostics(), annotations);

        internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new TestAtomSyntax(this, parent, position);
    }

    public sealed class List : Node
    {
        private readonly GreenNode _openParenToken;
        private readonly GreenNode? _values;
        private readonly GreenNode _closeParenToken;

        public List(GreenNode openParenToken, GreenNode? values, GreenNode closeParenToken)
            : this(openParenToken, values, closeParenToken, diagnostics: null, annotations: null)
        {
        }

        private List(GreenNode openParenToken, GreenNode? values, GreenNode closeParenToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
            : base(TestSyntaxKind.TestList, diagnostics, annotations)
        {
            SlotCount = 3;
            AdjustFlagsAndWidth(openParenToken);
            _openParenToken = openParenToken;
            AdjustFlagsAndWidth(values);
            _values = values;
            AdjustFlagsAndWidth(closeParenToken);
            _closeParenToken = closeParenToken;
        }

        internal override GreenNode? GetSlot(int index) => index switch
        {
            0 => _openParenToken,
            1 => _values,
            2 => _closeParenToken,
            _ => null,
        };

        internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new List(slots[0]!, slots[1], slots[2]!, GetDiagnostics(), GetAnnotations());

        internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new List(_openParenToken, _values, _closeParenToken, diagnostics, GetAnnotations());
        internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new List(_openParenToken, _values, _closeParenToken, GetDiagnostics(), annotations);

        internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new TestListSyntax(this, parent, position);
    }

    public sealed class Root : Node
    {
        private readonly GreenNode? _value;
        private readonly GreenNode _endOfFileToken;

        public Root(GreenNode? value, GreenNode endOfFileToken)
            : this(value, endOfFileToken, diagnostics: null, annotations: null)
        {
        }

        private Root(GreenNode? value, GreenNode endOfFileToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
            : base(TestSyntaxKind.TestRoot, diagnostics, annotations)
        {
            SlotCount = 2;
            AdjustFlagsAndWidth(value);
            _value = value;
            AdjustFlagsAndWidth(endOfFileToken);
            _endOfFileToken = endOfFileToken;
        }

        internal override GreenNode? GetSlot(int index) => index switch
        {
            0 => _value,
            1 => _endOfFileToken,
            _ => null,
        };

        internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new Root(slots[0], slots[1]!, GetDiagnostics(), GetAnnotations());

        internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new Root(_value, _endOfFileToken, diagnostics, GetAnnotations());
        internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new Root(_value, _endOfFileToken, GetDiagnostics(), annotations);

        internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new TestRootSyntax(this, parent, position);
    }
}
