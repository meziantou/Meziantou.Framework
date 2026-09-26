// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

internal static class TomlReaderExtensions
{
    public static T AlsoAdvance<T>(this T value, TomlReader reader)
    {
        reader.Read();
        return value;
    }
}
