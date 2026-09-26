// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using Tomlyn.Helpers;
using Tomlyn.Model;

namespace Tomlyn.Syntax
{
    /// <summary>
    /// A standard TOML table syntax node.
    /// </summary>
    public sealed class TableSyntax : TableSyntaxBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TableSyntax"/> class.
        /// </summary>
        public TableSyntax() : base(SyntaxKind.Table)
        {
        }

        /// <summary>
        /// Initializes a new table with the specified name.
        /// </summary>
        /// <param name="name">The table name.</param>
        public TableSyntax(string name) : this()
        {
            ArgumentNullException.ThrowIfNull(name);
            OpenBracket = SyntaxFactory.Token(TokenKind.OpenBracket);
            Name = new KeySyntax(name);
            CloseBracket = SyntaxFactory.Token(TokenKind.CloseBracket);
            EndOfLineToken = SyntaxFactory.NewLine();
        }

        /// <summary>
        /// Initializes a new table with the specified key syntax.
        /// </summary>
        /// <param name="name">The table name syntax.</param>
        public TableSyntax(KeySyntax name) : this()
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            OpenBracket = SyntaxFactory.Token(TokenKind.OpenBracket);
            CloseBracket = SyntaxFactory.Token(TokenKind.CloseBracket);
            EndOfLineToken = SyntaxFactory.NewLine();
        }

        /// <inheritdoc />
        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.Visit(this);
        }

        internal override TokenKind OpenTokenKind => TokenKind.OpenBracket;

        internal override TokenKind CloseTokenKind => TokenKind.CloseBracket;

        /// <inheritdoc />
        protected override string ToDebuggerDisplay()
        {
            return $"{base.ToDebuggerDisplay()} [{(Name is not null ? Name.ToString() : string.Empty)}]";
        }
    }
}