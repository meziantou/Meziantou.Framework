using System.Text;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>A key/value pair such as <c>name=value</c> or <c>name: value</c>.</summary>
public sealed class IniPropertySyntax : IniEntrySyntax
{
    internal IniPropertySyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken KeyToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the key, without the whitespace around it.</summary>
    public string Key => KeyToken.ValueText;

    /// <summary>Gets <c>=</c> or <c>:</c>, or a missing token when the line holds only a key.</summary>
    public SyntaxToken SeparatorToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>
    /// Gets the token of the first line of the value, whose text is that line as written, or a missing token when the line
    /// holds only a key.
    /// </summary>
    public SyntaxToken ValueToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Gets the tokens of the lines the value continues on, one per line.</summary>
    /// <remarks>
    /// A document read with <see cref="IniParseOptions.AllowMultilineValues"/> continues a value on the lines below it that
    /// are indented more than its key. The blank lines and comment lines between them are the leading trivia of the token
    /// that follows them. Otherwise, this is empty.
    /// </remarks>
    public SyntaxTokenList ContinuationTokens => new(this, Green.GetSlot(3), GetChildPosition(3), GetChildIndex(3));

    /// <summary>Gets the value.</summary>
    /// <remarks>
    /// The value is the text after the separator, without the whitespace around it or the comment after it. When the whole
    /// of it is in quotes, such as <c>"a;b"</c>, and <see cref="IniParseOptions.AllowQuotedValues"/> is set, the quotes are
    /// left out; nothing is escaped inside them. A value that continues on the lines below it is those lines joined with
    /// <c>\n</c>, each read the same way and without its indentation; a blank line between them is an empty line, and a
    /// comment line is left out. A line holding only a key has the empty string.
    /// </remarks>
    public string Value
    {
        get
        {
            var value = ValueToken.ValueText;
            var continuations = ContinuationTokens;
            if (continuations.Count == 0)
                return value;

            var builder = new StringBuilder(value);
            foreach (var token in continuations)
            {
                var lineHasComment = false;
                foreach (var trivia in token.LeadingTrivia)
                {
                    if (trivia.IsKind(SyntaxKind.CommentTrivia))
                    {
                        lineHasComment = true;
                    }
                    else if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                    {
                        if (!lineHasComment)
                        {
                            builder.Append('\n');
                        }

                        lineHasComment = false;
                    }
                }

                builder.Append('\n').Append(token.ValueText);
            }

            return builder.ToString();
        }
    }

    /// <summary>Returns this property with the given parts and the lines its value continues on, or itself when nothing changed.</summary>
    public IniPropertySyntax Update(SyntaxToken keyToken, SyntaxToken separatorToken, SyntaxToken valueToken)
        => Update(keyToken, separatorToken, valueToken, ContinuationTokens);

    /// <summary>Returns this property with the given parts, or itself when nothing changed.</summary>
    public IniPropertySyntax Update(SyntaxToken keyToken, SyntaxToken separatorToken, SyntaxToken valueToken, SyntaxTokenList continuationTokens)
    {
        if (keyToken.Node == Green.GetSlot(0) && separatorToken.Node == Green.GetSlot(1) && valueToken.Node == Green.GetSlot(2) && continuationTokens.Node == Green.GetSlot(3))
            return this;

        return SyntaxFactory.IniProperty(keyToken, separatorToken, valueToken, continuationTokens).WithAnnotationsFrom(this);
    }

    public IniPropertySyntax WithKeyToken(SyntaxToken keyToken) => Update(keyToken, SeparatorToken, ValueToken, ContinuationTokens);

    /// <summary>Returns this property with a new key.</summary>
    /// <remarks>
    /// In a document, the key is checked the way the document reads it (see <see cref="IniDocumentSyntax.Options"/>), as
    /// <see cref="SyntaxFactory.Key(string, IniParseOptions)"/> checks it. Otherwise it is checked as <see cref="SyntaxFactory.Key(string)"/> checks it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> cannot be written as a key.</exception>
    public IniPropertySyntax WithKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var options = (Parent as IniDocumentSyntax)?.Options ?? SyntaxFactory.StrictOptions;
        var token = SyntaxFactory.TryKey(key, options) ?? throw new ArgumentException($"'{key}' cannot be written as an INI key.", nameof(key));
        return WithKeyToken(token.WithTriviaFrom(KeyToken));
    }

    public IniPropertySyntax WithSeparatorToken(SyntaxToken separatorToken) => Update(KeyToken, separatorToken, ValueToken, ContinuationTokens);
    public IniPropertySyntax WithValueToken(SyntaxToken valueToken) => Update(KeyToken, SeparatorToken, valueToken, ContinuationTokens);
    public IniPropertySyntax WithContinuationTokens(SyntaxTokenList continuationTokens) => Update(KeyToken, SeparatorToken, ValueToken, continuationTokens);

    /// <summary>Returns this property with a new value, keeping the trivia around the old one.</summary>
    /// <remarks>
    /// <para>
    /// In a document, the value is written the way the document reads it (see <see cref="IniDocumentSyntax.Options"/>), and
    /// quoted only when those options require it. A property that is not part of a document has no options to go by, so
    /// its value is quoted as <see cref="SyntaxFactory.Value(string)"/> quotes it.
    /// </para>
    /// <para>
    /// A value with line breaks continues on the lines below the key, which only a document read with
    /// <see cref="IniParseOptions.AllowMultilineValues"/> allows. They are indented as the lines the old value continued
    /// on were, or four spaces more than the key, and the comment lines between the old lines stay in front of the new
    /// line in the same place. <see cref="Value"/> joins the lines with <c>\n</c>, whichever of <c>\r\n</c>, <c>\r</c>, or
    /// <c>\n</c> separated them. A line that held only a key gets <c>=</c> between the key and the value.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> cannot be written as a value; see <see cref="SyntaxFactory.Value(string, IniParseOptions)"/>.
    /// Or it cannot be written in front of the comment that follows the old value on its line, which would read as part of it.
    /// </exception>
    public IniPropertySyntax WithValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var separator = SeparatorToken.IsMissing ? SyntaxFactory.Token(SyntaxKind.EqualsToken) : SeparatorToken;
        var oldFirst = ValueToken;
        var oldContinuations = ContinuationTokens;
        if (Parent is not IniDocumentSyntax document)
        {
            var token = SyntaxFactory.Value(value).WithTriviaFrom(oldFirst);
            EnsureReadsBack(token, SyntaxFactory.StrictOptions, isContinuation: false, value, nameof(value));
            return Update(KeyToken, separator, token, default);
        }

        var options = document.Options;
        var endOfLine = SyntaxFactory.GetEndOfLine(this);
        var indentation = oldContinuations.Count > 0 ? GetIndentation(oldContinuations[0].LeadingTrivia) : GetIndentation(KeyToken.IsMissing ? SeparatorToken.LeadingTrivia : KeyToken.LeadingTrivia) + "    ";
        var (first, continuations) = SyntaxFactory.ValueLines(value, options, indentation, endOfLine, nameof(value));
        first = first.WithLeadingTrivia(oldFirst.LeadingTrivia);
        var oldTrailing = oldFirst.TrailingTrivia;
        if (continuations.Length == 0)
        {
            // The lines the old value continued on go, and what ended the last of them ends the new value.
            var trailing = oldContinuations.Count == 0
                ? oldTrailing
                : [.. oldTrailing.Where(trivia => !trivia.IsKind(SyntaxKind.EndOfLineTrivia)), .. oldContinuations[^1].TrailingTrivia.Where(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))];
            first = first.WithTrailingTrivia(trailing);
            EnsureReadsBack(first, options, isContinuation: false, value, nameof(value));
            return Update(KeyToken, separator, first, default);
        }

        // The first line keeps the comment it had. What ended the old value, a line break or the end of the text, ends the
        // new one.
        var endsLine = oldTrailing.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        first = endsLine ? first.WithTrailingTrivia(oldTrailing) : first.WithTrailingTrivia([.. oldTrailing, endOfLine]);
        for (var i = 0; i < continuations.Length && i < oldContinuations.Count; i++)
        {
            var comments = CommentLines(oldContinuations[i].LeadingTrivia);
            if (comments.Count > 0)
            {
                continuations[i] = continuations[i].WithLeadingTrivia([.. comments, .. continuations[i].LeadingTrivia]);
            }
        }

        if (oldContinuations.Count > 0)
        {
            continuations[^1] = continuations[^1].WithTrailingTrivia(oldContinuations[^1].TrailingTrivia);
        }
        else if (!endsLine)
        {
            continuations[^1] = continuations[^1].WithTrailingTrivia(SyntaxFactory.TriviaList());
        }

        var lines = SyntaxFactory.SplitLines(value);
        EnsureReadsBack(first, options, isContinuation: false, lines[0], nameof(value));
        EnsureReadsBack(continuations[^1], options, isContinuation: true, lines[^1], nameof(value));

        return Update(KeyToken, separator, first, SyntaxFactory.TokenList(continuations));
    }

    /// <summary>Throws when a line of a new value, with the trivia it keeps, would not read back as <paramref name="line"/>.</summary>
    private static void EnsureReadsBack(SyntaxToken token, IniParseOptions options, bool isContinuation, string line, string parameterName)
    {
        if (!SyntaxFactory.ReadsBackAs(token, options, isContinuation, line))
            throw new ArgumentException("The value cannot be written in front of the comment that follows it, which would read as part of it.", parameterName);
    }

    /// <summary>Gets the lines of <paramref name="trivia"/> that hold a comment, with their line breaks.</summary>
    private static List<SyntaxTrivia> CommentLines(SyntaxTriviaList trivia)
    {
        var result = new List<SyntaxTrivia>();
        var line = new List<SyntaxTrivia>();
        var lineHasComment = false;
        foreach (var item in trivia)
        {
            line.Add(item);
            if (item.IsKind(SyntaxKind.CommentTrivia))
            {
                lineHasComment = true;
            }
            else if (item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                if (lineHasComment)
                {
                    result.AddRange(line);
                }

                line.Clear();
                lineHasComment = false;
            }
        }

        return result;
    }

    /// <summary>Gets the whitespace at the end of <paramref name="trivia"/>, which indents the token it is in front of.</summary>
    internal static string GetIndentation(SyntaxTriviaList trivia)
    {
        var indentation = "";
        foreach (var item in trivia)
        {
            indentation = item.IsKind(SyntaxKind.WhitespaceTrivia) ? indentation + item.ToFullString() : "";
        }

        return indentation.Replace("﻿", "", StringComparison.Ordinal);
    }

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(IniSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitIniProperty(this);
    }

    public override TResult? Accept<TResult>(IniSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitIniProperty(this);
    }
}
