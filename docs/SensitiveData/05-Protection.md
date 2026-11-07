---
title: Protection
description: How the contents are protected on each platform, and what the type does not protect against.
---

[SensitiveData\<T\>](cref:Meziantou.Framework.SensitiveData{T}) stores its contents in native memory, outside the managed heap: the garbage collector never moves
or copies them, and [Dispose](cref:Meziantou.Framework.SensitiveData{T}.Dispose) can clear them. While the contents are not revealed, they are protected in a way that
depends on the platform.

## Protection by platform

| Platform | While the contents are not revealed |
| --- | --- |
| Windows | Allocated on a private heap and encrypted with `CryptProtectMemory`. |
| Linux and macOS | Mapped with `mmap` and made inaccessible with `mprotect(PROT_NONE)`. Locked in physical memory with `mlock` when possible. Not encrypted. |
| Every other platform (Android, iOS, tvOS, Mac Catalyst, FreeBSD, WebAssembly, ...) | Combined with a random key of the same length, stored in the same process. |

On Linux and macOS:

- Locking the memory with `mlock` often fails when the `RLIMIT_MEMLOCK` limit is low, which is the default in many
  container images. The failure is not reported, and the contents may then be written to swap.
- On Linux, the pages are excluded from core dumps (`madvise(MADV_DONTDUMP)`). macOS has no equivalent: a core dump
  taken while the contents are revealed contains them in clear.
- Each instance maps at least one memory page, and each reveal changes the protection of its pages twice. Prefer a
  moderate number of long-lived secrets to many small or frequently revealed ones.

On the other platforms, the random key only guards against a casual look at the memory: whoever can read the memory of
the process can read the key too. Android, iOS and Mac Catalyst use this protection, not the `mmap` one, even though
they support it.

Failing to apply or to lift the protection throws an exception: the contents are never silently left unprotected.

## What it protects against

- **Formatting and logging.** `ToString` returns the type name, so string interpolation, `string.Format` and loggers
  do not show the contents.
- **Serialization.** `System.Text.Json` serializes an instance as an empty object (`{}`), and `BinaryFormatter`
  refuses to serialize it.
- **Conversion to a string.** The [type converter](04-Configuration.md#use-the-type-converter) throws instead of
  converting an instance to a string.
- **Reading the memory of the process** while the contents are not revealed, within the limits of the platform
  described above.

## What it does not protect against

- **Code that reveals the contents.** Any code that has the instance can call a `Reveal` method. The type prevents
  accidents, not intentional disclosure.
- **A debugger or a memory dump taken during a reveal.** The contents are in clear for the duration of the reveal.
- **The copies you make.** The string returned by [RevealToString](cref:Meziantou.Framework.SensitiveData.RevealToString), the array returned by [RevealToArray](cref:Meziantou.Framework.SensitiveData{T}.RevealToArray), and the value
  passed to [Create](cref:Meziantou.Framework.SensitiveData.Create) are ordinary managed objects. See
  [Choose a reveal method](03-RevealingTheContents.md#choose-a-reveal-method).
