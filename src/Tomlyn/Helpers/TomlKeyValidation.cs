// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace Tomlyn.Helpers;

internal static class TomlKeyValidation
{
    public static bool IsValidKeyName(string key)
    {
        if (key is null)
        {
            return false;
        }

        if (key.Length == 0)
        {
            return false;
        }

        // TOML quoted keys are basic strings; writers can escape almost any character.
        // The one thing we must reject at the API boundary is invalid UTF-16 (unpaired surrogates),
        // which cannot be emitted as valid UTF-8.
        for (var i = 0; i < key.Length; i++)
        {
            var ch = key[i];
            if (!char.IsSurrogate(ch))
            {
                continue;
            }

            if (!char.IsHighSurrogate(ch))
            {
                return false;
            }

            if (i + 1 >= key.Length || !char.IsLowSurrogate(key[i + 1]))
            {
                return false;
            }

            i++;
        }

        return true;
    }
}

