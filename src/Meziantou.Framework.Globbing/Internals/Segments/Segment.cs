using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal abstract class Segment
    {
        public abstract bool Match(ReadOnlySpan<char> segment);
    }
}
