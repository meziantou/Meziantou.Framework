using System.Reflection.Metadata;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Assertions.Architecture.Reading;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct ILTokenReference(int Offset, ILTokenKind Kind, EntityHandle Handle);
