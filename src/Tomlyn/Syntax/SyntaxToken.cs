// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using Tomlyn.Helpers;
using Tomlyn.Model;

namespace Tomlyn.Syntax
{
    /// <summary>
    /// A token node.
    /// </summary>
    public class SyntaxToken : SyntaxNode
    {
        /// <summary>
        /// Creates a new instance of <see cref="SyntaxToken"/>
        /// </summary>
        public SyntaxToken() : base(SyntaxKind.Token)
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="SyntaxToken"/>
        /// </summary>
        /// <param name="tokenKind">The type of token</param>
        /// <param name="text">The associated textual representation</param>
        public SyntaxToken(TokenKind tokenKind, string? text) : this()
        {
            TokenKind = tokenKind;
            Text = text;
        }

        /// <summary>
        /// Gets or sets the kind of token.
        /// </summary>
        public TokenKind TokenKind { get; set; }

        /// <summary>
        /// Gets or sets the associated text
        /// </summary>
        public string? Text { get; set; }

        /// <inheritdoc />
        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override int ChildrenCount => 0;

        /// <inheritdoc />
        protected override SyntaxNode? GetChildImpl(int index)
        {
            return null;
        }

        /// <inheritdoc />
        protected override string ToDebuggerDisplay()
        {
            return $"{base.ToDebuggerDisplay()}: {TokenKind} {(Text is not null ? TomlFormatHelper.ToString(Text, TomlPropertyDisplayKind.Default) : string.Empty)}";
        }
    }
}