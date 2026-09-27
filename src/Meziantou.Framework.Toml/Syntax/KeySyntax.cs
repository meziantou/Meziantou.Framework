using System;

namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// A key TOML syntax node.
/// </summary>
public sealed class KeySyntax : ValueSyntax
{
    private BareKeyOrStringValueSyntax? _key;

    // Created on first use: most keys are not dotted, and a list per key is a large part of the tree size
    private SyntaxList<DottedKeyItemSyntax>? _dotKeys;

    /// <summary>
    /// Creates a new instance of a <see cref="KeySyntax"/>
    /// </summary>
    public KeySyntax() : base(SyntaxKind.Key)
    {
    }

    /// <summary>
    /// Creates a new instance of a <see cref="KeySyntax"/>
    /// </summary>
    /// <param name="key">A simple name of this key</param>
    public KeySyntax(string key) : this()
    {
        ArgumentNullException.ThrowIfNull(key);
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
    public SyntaxList<DottedKeyItemSyntax> DotKeys => _dotKeys ??= new SyntaxList<DottedKeyItemSyntax>() { Parent = this };

    internal SyntaxList<DottedKeyItemSyntax>? DotKeysIfCreated => _dotKeys;

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
        return _dotKeys;
    }

    /// <inheritdoc />
    protected override string ToDebuggerDisplay()
    {
        return $"{base.ToDebuggerDisplay()}: {ToString()}";
    }
}
