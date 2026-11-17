namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>The match finder of the LZMA encoder.</summary>
internal enum LzmaMatchFinderKind
{
    /// <summary>A hash chain of 2, 3 and 4-byte hashes: fast, used by the fast mode.</summary>
    HashChain4,

    /// <summary>A binary tree with 2, 3 and 4-byte hashes: finds better matches, used by the normal mode.</summary>
    BinaryTree4,
}
