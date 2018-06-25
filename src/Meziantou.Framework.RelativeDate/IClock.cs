using System;

namespace Meziantou.Framework.RelativeDate
{
    internal interface IClock
    {
        DateTime Now { get; }
        DateTime UtcNow { get; }
    }
}
