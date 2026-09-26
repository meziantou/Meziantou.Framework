// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Globalization;
using Tomlyn.Helpers;

namespace Tomlyn.Syntax
{
    /// <summary>
    /// A datetime TOML value syntax node.
    /// </summary>
    public sealed class DateTimeValueSyntax : ValueSyntax
    {
        private SyntaxToken? _token;

        /// <summary>
        /// Creates an instance of <see cref="DateTimeValueSyntax"/>
        /// </summary>
        /// <param name="kind">The kind of datetime</param>
        public DateTimeValueSyntax(SyntaxKind kind) : base(CheckDateTimeKind(kind))
        {
        }

        /// <summary>
        /// Gets or sets the datetime token.
        /// </summary>
        public SyntaxToken? Token
        {
            get => _token;
            set => ParentToThis(ref _token, value, value != null && value.TokenKind.IsDateTime(), $"The token kind `{value?.TokenKind}` is not a datetime token");
        }

        /// <summary>
        /// Gets or sets the parsed datetime value.
        /// </summary>
        public TomlDateTime Value { get; set; }

        /// <inheritdoc />
        public override int ChildrenCount => 1;

        /// <inheritdoc />
        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        protected override SyntaxNode? GetChildImpl(int index)
        {
            return Token;
        }

        private static SyntaxKind CheckDateTimeKind(SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.OffsetDateTimeByZ:
                case SyntaxKind.OffsetDateTimeByNumber:
                case SyntaxKind.LocalDateTime:
                case SyntaxKind.LocalDate:
                case SyntaxKind.LocalTime:
                    return kind;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <inheritdoc />
        protected override string ToDebuggerDisplay()
        {
            return $"{base.ToDebuggerDisplay()}: {TomlFormatHelper.ToString(Value)}";
        }
    }
}