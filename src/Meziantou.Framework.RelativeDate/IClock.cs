#nullable disable
using System;

namespace Meziantou.Framework
{
    internal interface IClock
    {
        DateTime Now { get; }
        DateTime UtcNow { get; }
    }
}
