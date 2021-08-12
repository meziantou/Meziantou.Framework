using System;

namespace Meziantou.Framework;

[Flags]
public enum ResettableCancellationTokenSourceOptions
{
    None = 0x0,
    CancelOnReset = 0x1,
    CancelOnDispose = 0x2,
}
