using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// Base class used to define a TOML Syntax tree.
/// </summary>
public abstract class SyntaxNode : SyntaxNodeBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyntaxNode"/> class.
    /// </summary>
    /// <param name="kind">The kind of syntax node.</param>
    protected SyntaxNode(SyntaxKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the type of node.
    /// </summary>
    public SyntaxKind Kind { get; }

    /// <summary>
    /// Gets the leading trivia attached to this node. Might be null if no leading trivias.
    /// </summary>
#pragma warning disable CA1002 // List<T> is part of the public API
    public List<SyntaxTrivia>? LeadingTrivia { get; set; }
#pragma warning restore CA1002

    /// <summary>
    /// Gets the trailing trivia attached to this node. Might be null if no trailing trivias.
    /// </summary>
#pragma warning disable CA1002 // List<T> is part of the public API
    public List<SyntaxTrivia>? TrailingTrivia { get; set; }
#pragma warning restore CA1002

    /// <summary>
    /// Gets the number of children.
    /// </summary>
    public abstract int ChildrenCount { get; }

    /// <summary>
    /// Gets a child at the specified index.
    /// </summary>
    /// <param name="index">Index of the child</param>
    /// <returns>A child at the specified index</returns>
    public SyntaxNode? GetChild(int index)
    {
        if (index < 0) throw ThrowHelper.GetIndexNegativeArgumentOutOfRangeException(nameof(index));
        if (index >= ChildrenCount) throw ThrowHelper.GetIndexArgumentOutOfRangeException(nameof(index), ChildrenCount);
        return GetChildImpl(index);
    }

    /// <summary>
    /// Gets a child at the specified index.
    /// </summary>
    /// <param name="index">Index of the child</param>
    /// <returns>A child at the specified index</returns>
    /// <remarks>The index is safe to use</remarks>
    protected abstract SyntaxNode? GetChildImpl(int index);

    /// <inheritdoc />
    public override string ToString()
    {
        using var writer = new StringWriter();
        WriteTo(writer);
        return writer.ToString();
    }

    /// <summary>
    /// Writes this node to a textual TOML representation
    /// </summary>
    /// <param name="writer">A writer to receive the TOML output</param>
    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteToInternal(writer);
    }

    // A loop rather than a recursion: the tree of a deeply nested document can be deeper than the stack allows
    private void WriteToInternal(TextWriter writer)
    {
        var pending = new Stack<(SyntaxNode Node, int NextChild)>();
        var afterComment = false;
        pending.Push((this, -1));
        while (pending.Count > 0)
        {
            var (node, nextChild) = pending.Pop();
            if (nextChild < 0)
            {
                if (node is DocumentSyntax { HasByteOrderMark: true })
                {
                    writer.Write('\uFEFF');
                }

                WriteTriviaTo(node.LeadingTrivia, writer, ref afterComment);
                if (node is InvalidSyntaxToken invalidToken)
                {
                    // The text that was found instead of the expected token
                    Write(writer, invalidToken.Text, isComment: false, ref afterComment);
                    WriteTriviaTo(node.TrailingTrivia, writer, ref afterComment);
                    continue;
                }

                if (node is SyntaxToken token)
                {
                    Write(writer, token.TokenKind.ToText() ?? token.Text, isComment: false, ref afterComment);
                    WriteTriviaTo(node.TrailingTrivia, writer, ref afterComment);
                    continue;
                }

                nextChild = 0;
            }

            if (nextChild < node.ChildrenCount)
            {
                pending.Push((node, nextChild + 1));
                if (node.GetChild(nextChild) is { } child)
                {
                    pending.Push((child, -1));
                }

                continue;
            }

            WriteTriviaTo(node.TrailingTrivia, writer, ref afterComment);
        }
    }

    private static void WriteTriviaTo(List<SyntaxTrivia>? trivias, TextWriter writer, ref bool afterComment)
    {
        if (trivias is null) return;
        foreach (var trivia in trivias)
        {
            if (trivia == null) continue;
            Write(writer, trivia.Text, trivia.Kind == TokenKind.Comment, ref afterComment);
        }
    }

    // A comment runs to the end of its line: what follows it, such as a node given a comment with AddLeadingComment or an
    // item after a comment in an inline table, starts on the next line instead of being commented out
    private static void Write(TextWriter writer, string? text, bool isComment, ref bool afterComment)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (afterComment && !isComment && text[0] is not '\n' and not '\r' && text.AsSpan().IndexOfAnyExcept(' ', '\t') >= 0)
        {
            writer.Write('\n');
            afterComment = false;
        }
        else if (text[0] is '\n' or '\r')
        {
            afterComment = false;
        }

        writer.Write(text);
        if (isComment)
        {
            afterComment = true;
        }
    }

    /// <summary>
    /// Helper method to deparent/parent a node to this instance.
    /// </summary>
    /// <typeparam name="TSyntaxNode">Type of the node</typeparam>
    /// <param name="set">The previous child node parented to this instance</param>
    /// <param name="node">The new child node to parent to this instance</param>
    protected void ParentToThis<TSyntaxNode>(ref TSyntaxNode? set, TSyntaxNode? node) where TSyntaxNode : SyntaxNode
    {
        if (node?.Parent != null) throw ThrowHelper.GetExpectingNoParentException();
        if (set != null)
        {
            set.Parent = null;
        }
        if (node != null)
        {
            node.Parent = this;
        }
        set = node;
    }

    /// <summary>
    /// Helper method to deparent/parent a <see cref="SyntaxToken"/> to this instance with an expected kind of token.
    /// </summary>
    /// <typeparam name="TSyntaxNode">Type of the node</typeparam>
    /// <param name="set">The previous child node parented to this instance</param>
    /// <param name="node">The new child node to parent to this instance</param>
    /// <param name="expectedKind">The expected kind of token</param>
    protected void ParentToThis<TSyntaxNode>(ref TSyntaxNode? set, TSyntaxNode? node, TokenKind expectedKind) where TSyntaxNode : SyntaxToken
    {
        ParentToThis(ref set, node, node?.TokenKind == expectedKind, expectedKind);
    }

    /// <summary>
    /// Helper method to deparent/parent a <see cref="SyntaxToken"/> to this instance with an expected kind of token condition.
    /// </summary>
    /// <typeparam name="TSyntaxNode">Type of the node</typeparam>
    /// <typeparam name="TExpected">The type of message</typeparam>
    /// <param name="set">The previous child node parented to this instance</param>
    /// <param name="node">The new child node to parent to this instance</param>
    /// <param name="expectedKindSuccess">true if kind is matching, false otherwise</param>
    /// <param name="expectedMessage">The message to display if the kind is not matching</param>
    protected void ParentToThis<TSyntaxNode, TExpected>(ref TSyntaxNode? set, TSyntaxNode? node, bool expectedKindSuccess, TExpected expectedMessage) where TSyntaxNode : SyntaxToken
    {
        if (node != null && !expectedKindSuccess) throw new InvalidOperationException($"Unexpected node kind `{node.TokenKind}` while expecting `{expectedMessage}`");
        ParentToThis(ref set, node);
    }

    /// <summary>
    /// Helper method to deparent/parent a <see cref="SyntaxToken"/> to this instance with an expected kind of token.
    /// </summary>
    /// <typeparam name="TSyntaxNode">Type of the node</typeparam>
    /// <param name="set">The previous child node parented to this instance</param>
    /// <param name="node">The new child node to parent to this instance</param>
    /// <param name="expectedKind1">The expected kind of token (option1)</param>
    /// <param name="expectedKind2">The expected kind of token (option2)</param>
    protected void ParentToThis<TSyntaxNode>(ref TSyntaxNode? set, TSyntaxNode? node, TokenKind expectedKind1, TokenKind expectedKind2) where TSyntaxNode : SyntaxToken
    {
        ParentToThis(ref set, node, node?.TokenKind == expectedKind1 || node?.TokenKind == expectedKind2, new ExpectedTuple2<TokenKind, TokenKind>(expectedKind1, expectedKind2));
    }

    private readonly struct ExpectedTuple2<T1, T2>
    {
        public ExpectedTuple2(T1 value1, T2 value2)
        {
            Value1 = value1;
            Value2 = value2;
        }

        public readonly T1 Value1;

        public readonly T2 Value2;

        public override string ToString()
        {
            return $"`{Value1}` or `{Value2}`";
        }
    }
}
