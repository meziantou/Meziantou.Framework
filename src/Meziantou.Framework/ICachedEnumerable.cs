using System;
using System.Collections.Generic;

namespace Meziantou.Framework;

public interface ICachedEnumerable<T> : IEnumerable<T>, IDisposable
{
}
