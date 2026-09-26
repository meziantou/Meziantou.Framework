// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using Tomlyn.Helpers;
using Tomlyn.Model;

namespace Tomlyn.Syntax
{
    /// <summary>
    /// Represents trivia (whitespace, comments, newlines) attached to syntax nodes.
    /// </summary>
    public sealed class SyntaxTrivia : SyntaxNodeBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyntaxTrivia"/> class.
        /// </summary>
        public SyntaxTrivia()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyntaxTrivia"/> class.
        /// </summary>
        /// <param name="kind">The trivia token kind.</param>
        /// <param name="text">The trivia text.</param>
        public SyntaxTrivia(TokenKind kind, string text)
        {
            if (!kind.IsTrivia()) throw new ArgumentOutOfRangeException(nameof(kind), $"The kind `{kind}` is not a trivia");
            Kind = kind;
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        /// <summary>
        /// Gets or sets the trivia kind.
        /// </summary>
        public TokenKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the trivia text.
        /// </summary>
        public string? Text { get; set; }

        /// <inheritdoc />
        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        protected override string ToDebuggerDisplay()
        {
            return $"{base.ToDebuggerDisplay()} {(Text is not null ? TomlFormatHelper.ToString(Text, TomlPropertyDisplayKind.Default) : string.Empty)}";
        }
    }
}