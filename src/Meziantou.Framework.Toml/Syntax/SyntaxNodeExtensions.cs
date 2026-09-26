using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// Extensions for <see cref="SyntaxNode"/>.
/// </summary>
public static class SyntaxNodeExtensions
{
    /// <summary>
    /// Get all <see cref="SyntaxToken"/> and <see cref="SyntaxTrivia"/> in Depth-First-Search order of the node.
    /// </summary>
    /// <param name="node">The node to collect all tokens from</param>
    /// <param name="includeCommentsAndWhitespaces"><c>true</c> to include comments and whitespaces.</param>
    /// <returns>All descendants in Depth-First-Search order of the node.</returns>
    public static IEnumerable<SyntaxNodeBase> Tokens(this SyntaxNode node, bool includeCommentsAndWhitespaces = true)
    {
        foreach (var token in Descendants(node, true))
        {
            if (token is SyntaxTrivia)
            {
                if (!includeCommentsAndWhitespaces) continue;
            }
            else if (token is not SyntaxToken) continue;

            yield return token;
        }
    }

    /// <summary>
    /// Get all descendants in Depth-First-Search order of the node. Note that this method returns the node itself (last).
    /// </summary>
    /// <param name="node">The node to collect all descendants</param>
    /// <param name="includeTokensCommentsAndWhitespaces"><c>true</c> to include tokens, comments and whitespaces.</param>
    /// <returns>All descendants in Depth-First-Search order of the node.</returns>
    public static IEnumerable<SyntaxNodeBase> Descendants(this SyntaxNode node, bool includeTokensCommentsAndWhitespaces = false)
    {
        if (!includeTokensCommentsAndWhitespaces && node is SyntaxToken) yield break;

        if (includeTokensCommentsAndWhitespaces && node.LeadingTrivia is not null)
        {
            foreach (var syntaxTrivia in node.LeadingTrivia)
            {
                yield return syntaxTrivia;
            }
        }

        var childrenCount = node.ChildrenCount;
        for (int i = 0; i < childrenCount; i++)
        {
            var child = node.GetChild(i);

            // Skip syntax list
            if (child is SyntaxList list)
            {
                var subChildrenCount = list.ChildrenCount;
                for (int j = 0; j < subChildrenCount; j++)
                {
                    var subChild = list.GetChild(j);
                    if (subChild is not null)
                    {
                        foreach (var sub in Descendants(subChild, includeTokensCommentsAndWhitespaces))
                        {
                            yield return sub;
                        }
                    }
                }
            }
            else if (child is not null)
            {
                foreach (var sub in Descendants(child, includeTokensCommentsAndWhitespaces))
                {
                    yield return sub;
                }
            }
        }

        yield return node;

        if (includeTokensCommentsAndWhitespaces && node.TrailingTrivia is not null)
        {
            foreach (var syntaxTrivia in node.TrailingTrivia)
            {
                yield return syntaxTrivia;
            }
        }
    }

    /// <summary>
    /// Adds an integer key/value pair to a list of key/value nodes.
    /// </summary>
    /// <param name="list">The target list.</param>
    /// <param name="name">The key name.</param>
    /// <param name="value">The integer value.</param>
    public static void Add(this SyntaxList<KeyValueSyntax> list, string name, int value)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.Add(new KeyValueSyntax(name, new IntegerValueSyntax(value)));
    }

    /// <summary>
    /// Adds a long key/value pair to a list of key/value nodes.
    /// </summary>
    /// <param name="list">The target list.</param>
    /// <param name="name">The key name.</param>
    /// <param name="value">The long value.</param>
    public static void Add(this SyntaxList<KeyValueSyntax> list, string name, long value)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.Add(new KeyValueSyntax(name, new IntegerValueSyntax(value)));
    }

    /// <summary>
    /// Adds a boolean key/value pair to a list of key/value nodes.
    /// </summary>
    /// <param name="list">The target list.</param>
    /// <param name="name">The key name.</param>
    /// <param name="value">The boolean value.</param>
    public static void Add(this SyntaxList<KeyValueSyntax> list, string name, bool value)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.Add(new KeyValueSyntax(name, new BooleanValueSyntax(value)));
    }

    /// <summary>
    /// Adds a floating-point key/value pair to a list of key/value nodes.
    /// </summary>
    /// <param name="list">The target list.</param>
    /// <param name="name">The key name.</param>
    /// <param name="value">The floating-point value.</param>
    public static void Add(this SyntaxList<KeyValueSyntax> list, string name, double value)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.Add(new KeyValueSyntax(name, new FloatValueSyntax(value)));
    }

    /// <summary>
    /// Adds a string key/value pair to a list of key/value nodes.
    /// </summary>
    /// <param name="list">The target list.</param>
    /// <param name="name">The key name.</param>
    /// <param name="value">The string value.</param>
    public static void Add(this SyntaxList<KeyValueSyntax> list, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.Add(new KeyValueSyntax(name, new StringValueSyntax(value)));
    }

    /// <summary>
    /// Adds an integer array key/value pair to a list of key/value nodes.
    /// </summary>
    /// <param name="list">The target list.</param>
    /// <param name="name">The key name.</param>
    /// <param name="values">The array of integers.</param>
    public static void Add(this SyntaxList<KeyValueSyntax> list, string name, int[] values)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.Add(new KeyValueSyntax(name, new ArraySyntax(values)));
    }

    /// <summary>
    /// Adds a string array key/value pair to a list of key/value nodes.
    /// </summary>
    /// <param name="list">The target list.</param>
    /// <param name="name">The key name.</param>
    /// <param name="values">The array of strings.</param>
    public static void Add(this SyntaxList<KeyValueSyntax> list, string name, string[] values)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.Add(new KeyValueSyntax(name, new ArraySyntax(values)));
    }

    /// <summary>
    /// Adds a date/time value node to a list of key/value nodes.
    /// </summary>
    /// <param name="list">The target list.</param>
    /// <param name="name">The key name.</param>
    /// <param name="value">The date/time value node.</param>
    public static void Add(this SyntaxList<KeyValueSyntax> list, string name, DateTimeValueSyntax value)
		{
        ArgumentNullException.ThrowIfNull(list);
        list.Add(new KeyValueSyntax(name, value));
		}

    /// <summary>
    /// Adds a trailing comment to a key/value node.
    /// </summary>
    /// <param name="keyValue">The key/value node.</param>
    /// <param name="comment">The comment text.</param>
    /// <returns>The same key/value node.</returns>
    public static KeyValueSyntax AddTrailingComment(this KeyValueSyntax keyValue, string comment)
    {
        ArgumentNullException.ThrowIfNull(keyValue);
        if (keyValue.Value == null) throw new InvalidOperationException("The Value must not be null on the KeyValueSyntax");
        keyValue.Value.AddTrailingWhitespace().AddTrailingComment(comment);
        return keyValue;
    }

    /// <summary>
    /// Adds leading whitespace trivia to the node.
    /// </summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="node">The target node.</param>
    /// <returns>The same node.</returns>
    public static T AddLeadingWhitespace<T>(this T node) where T : SyntaxNode
    {
        return AddLeadingTrivia(node, SyntaxFactory.Whitespace());
    }

    /// <summary>
    /// Adds trailing whitespace trivia to the node.
    /// </summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="node">The target node.</param>
    /// <returns>The same node.</returns>
    public static T AddTrailingWhitespace<T>(this T node) where T : SyntaxNode
    {
        return AddTrailingTrivia(node, SyntaxFactory.Whitespace());
    }

    /// <summary>
    /// Adds leading trivia to the node.
    /// </summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="node">The target node.</param>
    /// <param name="trivia">The trivia to add.</param>
    /// <returns>The same node.</returns>
    public static T AddLeadingTrivia<T>(this T node, SyntaxTrivia trivia) where T : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(node);
        var trivias = node.LeadingTrivia;
        if (trivias == null)
        {
            trivias = new List<SyntaxTrivia>();
            node.LeadingTrivia = trivias;
        }
        trivias.Add(trivia);
        return node;
    }

    /// <summary>
    /// Adds trailing trivia to the node.
    /// </summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="node">The target node.</param>
    /// <param name="trivia">The trivia to add.</param>
    /// <returns>The same node.</returns>
    public static T AddTrailingTrivia<T>(this T node, SyntaxTrivia trivia) where T : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(node);
        var trivias = node.TrailingTrivia;
        if (trivias == null)
        {
            trivias = new List<SyntaxTrivia>();
            node.TrailingTrivia = trivias;
        }
        trivias.Add(trivia);
        return node;
    }

    /// <summary>
    /// Adds a leading comment to the node.
    /// </summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="node">The target node.</param>
    /// <param name="comment">The comment text.</param>
    /// <returns>The same node.</returns>
    public static T AddLeadingComment<T>(this T node, string comment) where T : SyntaxNode
    {
        return AddLeadingTrivia(node, SyntaxFactory.Comment(comment));
    }

    /// <summary>
    /// Adds a trailing comment to the node.
    /// </summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="node">The target node.</param>
    /// <param name="comment">The comment text.</param>
    /// <returns>The same node.</returns>
    public static T AddTrailingComment<T>(this T node, string comment) where T : SyntaxNode
    {
        return AddTrailingTrivia(node, SyntaxFactory.Comment(comment));
    }

    /// <summary>
    /// Adds a leading newline trivia to the node.
    /// </summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="node">The target node.</param>
    /// <returns>The same node.</returns>
    public static T AddLeadingTriviaNewLine<T>(this T node) where T : SyntaxNode
    {
        return AddLeadingTrivia(node, SyntaxFactory.NewLineTrivia());
    }

    /// <summary>
    /// Adds a trailing newline trivia to the node.
    /// </summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="node">The target node.</param>
    /// <returns>The same node.</returns>
    public static T AddTrailingTriviaNewLine<T>(this T node) where T : SyntaxNode
    {
        return AddTrailingTrivia(node, SyntaxFactory.NewLineTrivia());
    }
}
