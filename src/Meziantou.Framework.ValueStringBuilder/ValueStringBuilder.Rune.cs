using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

// https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.Uri/src/System/ValueStringBuilderExtensions.cs
#if PUBLIC_VALUESTRINGBUILDER
public
#else
internal
#endif
ref partial struct ValueStringBuilder
{
    /// <summary>
    /// Appends a Unicode scalar value, encoded as one character or as a surrogate pair.
    /// </summary>
    /// <param name="rune">The scalar value to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(Rune rune)
    {
        var pos = _pos;
        var chars = _chars;
        // Two free characters are needed for a surrogate pair; pos < chars.Length follows from pos + 1 < chars.Length.
        if ((uint)(pos + 1) < (uint)chars.Length)
        {
            if (rune.Value <= 0xFFFF)
            {
                chars[pos] = (char)rune.Value;
                _pos = pos + 1;
            }
            else
            {
                chars[pos] = (char)((rune.Value + ((0xD800u - 0x40u) << 10)) >> 10);
                chars[pos + 1] = (char)((rune.Value & 0x3FFu) + 0xDC00u);
                _pos = pos + 2;
            }
        }
        else
        {
            GrowAndAppend(rune);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowAndAppend(Rune rune)
    {
        if (rune.Value <= 0xFFFF)
        {
            Append((char)rune.Value);
        }
        else
        {
            Grow(2);
            Append(rune);
        }
    }
}
