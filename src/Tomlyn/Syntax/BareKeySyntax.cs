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
    /// A TOML bare key syntax node.
    /// </summary>
    public sealed class BareKeySyntax : BareKeyOrStringValueSyntax
    {
        private SyntaxToken? _key;

        /// <summary>
        /// Creates a new instance of a <see cref="BareKeySyntax"/>
        /// </summary>
        public BareKeySyntax() : base(SyntaxKind.BasicKey)
        {
        }

        /// <summary>
        /// Creates a new instance of a <see cref="BareKeySyntax"/>
        /// </summary>
        /// <param name="name">The name used for this key</param>
        public BareKeySyntax(string name) : this()
        {
            if (!IsBareKey(name)) throw new ArgumentOutOfRangeException($"The key `{name}` does not contain valid characters [A-Za-z0-9_\\-]");
            Key = new SyntaxToken(TokenKind.BasicKey, name);
        }

        /// <summary>
        /// A textual representation of the key
        /// </summary>
        public SyntaxToken? Key
        {
            get => _key;
            set => ParentToThis(ref _key, value, TokenKind.BasicKey);
        }

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
            return Key;
        }

        /// <summary>
        /// Determines whether the specified string is a valid bare key.
        /// </summary>
        /// <param name="name">The candidate key.</param>
        /// <returns><c>true</c> if the string is a valid bare key; otherwise <c>false</c>.</returns>
        public static bool IsBareKey(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (name.Length == 0 || string.IsNullOrWhiteSpace(name)) return false;
            foreach (var c in name)
            {
                if (!CharHelper.IsKeyContinue(c))
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc />
        protected override string ToDebuggerDisplay()
        {
            return $"{base.ToDebuggerDisplay()}: {(Key is not null ? TomlFormatHelper.ToString(Key.ToString(), TomlPropertyDisplayKind.Default) : string.Empty)}";
        }
    }
}