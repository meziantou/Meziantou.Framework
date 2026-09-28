// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Helpers;

// Used to avoid the overhead of type covariance checks
internal readonly struct BlockWrapper(Block block) : IEquatable<BlockWrapper>
{
    public readonly Block Block = block;

    public static implicit operator Block(BlockWrapper wrapper) => wrapper.Block;

    public static implicit operator BlockWrapper(Block block) => new BlockWrapper(block);

    public bool Equals(BlockWrapper other) => ReferenceEquals(Block, other.Block);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BlockWrapper other && Equals(other);

    public override int GetHashCode() => Block.GetHashCode();
}
