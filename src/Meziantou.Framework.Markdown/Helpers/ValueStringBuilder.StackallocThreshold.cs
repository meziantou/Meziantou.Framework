namespace Meziantou.Framework;

internal ref partial struct ValueStringBuilder
{
#if DEBUG
    // A tiny buffer makes the tests exercise the code paths that grow the builder
    public const int StackallocThreshold = 7;
#else
    // NET5+ has SkipLocalsInit, so allocating more is "free"
    public const int StackallocThreshold = 256;
#endif
}
