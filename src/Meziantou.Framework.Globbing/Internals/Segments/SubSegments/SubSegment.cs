using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal abstract class SubSegment
    {
        public abstract bool Match(ReadOnlySpan<char> segment, out int readCharCount);
    }
}
