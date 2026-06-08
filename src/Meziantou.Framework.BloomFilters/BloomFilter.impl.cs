
namespace Meziantou.Framework.BloomFilters;

#pragma warning disable MA0048 // File name must match type name

partial class BloomFilter
{
    public static BloomFilterXXHash128 CreateXXHash128(BloomFilterSize size) => new(size.BitCount, size.HashCount);
    public static BloomFilterXXHash64 CreateXXHash64(BloomFilterSize size) => new(size.BitCount, size.HashCount);
    public static BloomFilterXXHash32 CreateXXHash32(BloomFilterSize size) => new(size.BitCount, size.HashCount);
    public static BloomFilterXXHash3 CreateXXHash3(BloomFilterSize size) => new(size.BitCount, size.HashCount);
}

partial class BloomFilterXXHash128
{
    internal BloomFilterXXHash128(long bitCount, int hashCount)
        : base(bitCount, hashCount)
    {
    }

    public void Add(Int32 value) => AddHash(Hash(value));
    public bool MayContain(Int32 value) => MayContainHash(Hash(value));
    public void Add(UInt32 value) => AddHash(Hash(value));
    public bool MayContain(UInt32 value) => MayContainHash(Hash(value));
    public void Add(Int64 value) => AddHash(Hash(value));
    public bool MayContain(Int64 value) => MayContainHash(Hash(value));
    public void Add(UInt64 value) => AddHash(Hash(value));
    public bool MayContain(UInt64 value) => MayContainHash(Hash(value));
    public void Add(Guid value) => AddHash(Hash(value));
    public bool MayContain(Guid value) => MayContainHash(Hash(value));
    public void Add(String value) => AddHash(Hash(value));
    public bool MayContain(String value) => MayContainHash(Hash(value));
    public void Add(UInt128 value) => AddHash(Hash(value));
    public bool MayContain(UInt128 value) => MayContainHash(Hash(value));
    public void Add(Int128 value) => AddHash(Hash(value));
    public bool MayContain(Int128 value) => MayContainHash(Hash(value));
}

partial class BloomFilterXXHash64
{
    internal BloomFilterXXHash64(long bitCount, int hashCount)
        : base(bitCount, hashCount)
    {
    }

    public void Add(Int32 value) => AddHash(Hash(value));
    public bool MayContain(Int32 value) => MayContainHash(Hash(value));
    public void Add(UInt32 value) => AddHash(Hash(value));
    public bool MayContain(UInt32 value) => MayContainHash(Hash(value));
    public void Add(Int64 value) => AddHash(Hash(value));
    public bool MayContain(Int64 value) => MayContainHash(Hash(value));
    public void Add(UInt64 value) => AddHash(Hash(value));
    public bool MayContain(UInt64 value) => MayContainHash(Hash(value));
    public void Add(Guid value) => AddHash(Hash(value));
    public bool MayContain(Guid value) => MayContainHash(Hash(value));
    public void Add(String value) => AddHash(Hash(value));
    public bool MayContain(String value) => MayContainHash(Hash(value));
    public void Add(UInt128 value) => AddHash(Hash(value));
    public bool MayContain(UInt128 value) => MayContainHash(Hash(value));
    public void Add(Int128 value) => AddHash(Hash(value));
    public bool MayContain(Int128 value) => MayContainHash(Hash(value));
}

partial class BloomFilterXXHash32
{
    internal BloomFilterXXHash32(long bitCount, int hashCount)
        : base(bitCount, hashCount)
    {
    }

    public void Add(Int32 value) => AddHash(Hash(value));
    public bool MayContain(Int32 value) => MayContainHash(Hash(value));
    public void Add(UInt32 value) => AddHash(Hash(value));
    public bool MayContain(UInt32 value) => MayContainHash(Hash(value));
    public void Add(Int64 value) => AddHash(Hash(value));
    public bool MayContain(Int64 value) => MayContainHash(Hash(value));
    public void Add(UInt64 value) => AddHash(Hash(value));
    public bool MayContain(UInt64 value) => MayContainHash(Hash(value));
    public void Add(Guid value) => AddHash(Hash(value));
    public bool MayContain(Guid value) => MayContainHash(Hash(value));
    public void Add(String value) => AddHash(Hash(value));
    public bool MayContain(String value) => MayContainHash(Hash(value));
    public void Add(UInt128 value) => AddHash(Hash(value));
    public bool MayContain(UInt128 value) => MayContainHash(Hash(value));
    public void Add(Int128 value) => AddHash(Hash(value));
    public bool MayContain(Int128 value) => MayContainHash(Hash(value));
}

partial class BloomFilterXXHash3
{
    internal BloomFilterXXHash3(long bitCount, int hashCount)
        : base(bitCount, hashCount)
    {
    }

    public void Add(Int32 value) => AddHash(Hash(value));
    public bool MayContain(Int32 value) => MayContainHash(Hash(value));
    public void Add(UInt32 value) => AddHash(Hash(value));
    public bool MayContain(UInt32 value) => MayContainHash(Hash(value));
    public void Add(Int64 value) => AddHash(Hash(value));
    public bool MayContain(Int64 value) => MayContainHash(Hash(value));
    public void Add(UInt64 value) => AddHash(Hash(value));
    public bool MayContain(UInt64 value) => MayContainHash(Hash(value));
    public void Add(Guid value) => AddHash(Hash(value));
    public bool MayContain(Guid value) => MayContainHash(Hash(value));
    public void Add(String value) => AddHash(Hash(value));
    public bool MayContain(String value) => MayContainHash(Hash(value));
    public void Add(UInt128 value) => AddHash(Hash(value));
    public bool MayContain(UInt128 value) => MayContainHash(Hash(value));
    public void Add(Int128 value) => AddHash(Hash(value));
    public bool MayContain(Int128 value) => MayContainHash(Hash(value));
}

