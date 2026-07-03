namespace Meziantou.Framework.Language.Json;

/// <summary>Represents invalid or skipped JSON text retained in the concrete syntax tree.</summary>
public sealed class JsonSkippedTextSyntax : JsonValueSyntax
{
    public JsonSkippedTextSyntax(IReadOnlyList<JsonSyntaxToken> tokens, int fullStart)
        : base(JsonSyntaxKind.JsonSkippedText, BuildFullText(tokens ?? []), fullStart, tokens)
    {
    }

    public string Text => ToFullString();

    public override void Accept(JsonSyntaxVisitor visitor) => visitor.VisitSkippedText(this);
    public override TResult Accept<TResult>(JsonSyntaxVisitor<TResult> visitor) => visitor.VisitSkippedText(this);
}
