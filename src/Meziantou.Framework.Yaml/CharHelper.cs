// Code from coreclr with MIT License
// https://github.com/dotnet/coreclr/blob/e3eecaa56ec08d47941bc7191656a7559ac8b3c0/src/mscorlib/shared/System/Char.cs#L1018
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Meziantou.Framework.Yaml;

internal static class CharHelper
{
    internal const char HIGH_SURROGATE_START = '\ud800';
    internal const char HIGH_SURROGATE_END = '\udbff';
    internal const char LOW_SURROGATE_START = '\udc00';

    internal const char LOW_SURROGATE_END = '\udfff';

    // The starting codepoint for Unicode plane 1.  Plane 1 contains 0x010000 ~ 0x01ffff.

    internal const int UNICODE_PLANE01_START = 0x10000;

    internal const int UNICODE_PLANE00_END = 0x00ffff;

    // The starting codepoint for Unicode plane 1.  Plane 1 contains 0x010000 ~ 0x01ffff.
    // The end codepoint for Unicode plane 16.  This is the maximum code point value allowed for Unicode.
    // Plane 16 contains 0x100000 ~ 0x10ffff.

    internal const int UNICODE_PLANE16_END = 0x10ffff;

    // char.IsHighSurrogate and char.IsLowSurrogate is not available on PCL 328

    public static bool IsHighSurrogate(char c)
    {
        return HIGH_SURROGATE_START <= c && c <= HIGH_SURROGATE_END;
    }

    public static bool IsLowSurrogate(char c)
    {
        return LOW_SURROGATE_START <= c && c <= LOW_SURROGATE_END;
    }

    public static int ConvertToUtf32(char highSurrogate, char lowSurrogate)
    {
        return (((highSurrogate - HIGH_SURROGATE_START) * 0x400) + (lowSurrogate - LOW_SURROGATE_START) + UNICODE_PLANE01_START);
    }

    public static string ConvertFromUtf32(int utf32)
    {
        return char.ConvertFromUtf32(utf32);
    }
}