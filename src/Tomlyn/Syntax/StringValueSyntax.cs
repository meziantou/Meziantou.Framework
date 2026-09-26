// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using Tomlyn.Helpers;
using Tomlyn.Model;
using Tomlyn.Text;

namespace Tomlyn.Syntax
{
    /// <summary>
    /// A string TOML syntax value node.
    /// </summary>
    public sealed class StringValueSyntax : BareKeyOrStringValueSyntax
    {
        private SyntaxToken? _token;

        /// <summary>
        /// Creates a new instance of <see cref="StringValueSyntax"/>
        /// </summary>
        public StringValueSyntax() : base(SyntaxKind.String)
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="StringValueSyntax"/>
        /// </summary>
        /// <param name="text">String value used for this node</param>
        public StringValueSyntax(string text) : this()
        {
            ArgumentNullException.ThrowIfNull(text);
            Token = new SyntaxToken(TokenKind.String, $"\"{text.EscapeForToml()}\"");
            Value = text;
        }

        /// <summary>
        /// The token of the string.
        /// </summary>
        public SyntaxToken? Token
        {
            get => _token;
            set => ParentToThis(ref _token, value, value != null && value.TokenKind.IsString(), "string");
        }

        /// <summary>
        /// The associated parsed string value
        /// </summary>
        public string? Value { get; set; }

        /// <inheritdoc />
        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override int ChildrenCount => 1;

        /// <inheritdoc />
        protected override SyntaxNode? GetChildImpl(int index)
        {
            return Token;
        }

        /// <inheritdoc />
        protected override string ToDebuggerDisplay()
        {
            return $"{base.ToDebuggerDisplay()}: {(Value is not null ? TomlFormatHelper.ToString(Value, TomlPropertyDisplayKind.Default) : string.Empty)}";
        }
    }
}