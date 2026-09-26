// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace Tomlyn.Syntax
{
    /// <summary>
    /// A key TOML syntax node.
    /// </summary>
    public sealed class KeySyntax : ValueSyntax
    {
        private BareKeyOrStringValueSyntax? _key;

        /// <summary>
        /// Creates a new instance of a <see cref="KeySyntax"/>
        /// </summary>
        public KeySyntax() : base(SyntaxKind.Key)
        {
            DotKeys = new SyntaxList<DottedKeyItemSyntax>() { Parent = this };
        }

        /// <summary>
        /// Creates a new instance of a <see cref="KeySyntax"/>
        /// </summary>
        /// <param name="key">A simple name of this key</param>
        public KeySyntax(string key) : this()
        {
            Key = BareKeySyntax.IsBareKey(key) ? (BareKeyOrStringValueSyntax)new BareKeySyntax(key) : new StringValueSyntax(key);
        }

        /// <summary>
        /// Creates a new instance of a <see cref="KeySyntax"/>
        /// </summary>
        /// <param name="key">the base key</param>
        /// <param name="dotKey1">the key after the dot</param>
        public KeySyntax(string key, string dotKey1) : this()
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(dotKey1);
            Key = BareKeySyntax.IsBareKey(key) ? (BareKeyOrStringValueSyntax)new BareKeySyntax(key) : new StringValueSyntax(key);
            DotKeys.Add(new DottedKeyItemSyntax(dotKey1));
        }

        /// <summary>
        /// The base of the key before the dot
        /// </summary>
        public BareKeyOrStringValueSyntax? Key
        {
            get => _key;
            set => ParentToThis(ref _key, value); // The key type (bare key or string) is not validated
        }

        /// <summary>
        /// List of the dotted keys.
        /// </summary>
        public SyntaxList<DottedKeyItemSyntax> DotKeys { get; }

        /// <inheritdoc />
        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override int ChildrenCount => 2;

        /// <inheritdoc />
        protected override SyntaxNode? GetChildImpl(int index)
        {
            if (index == 0) return Key;
            return DotKeys;
        }

        /// <inheritdoc />
        protected override string ToDebuggerDisplay()
        {
            return $"{base.ToDebuggerDisplay()}: {ToString()}";
        }
    }
}