using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal static class TomlReaderExtensions
{
    public static T AlsoAdvance<T>(this T value, TomlReader reader)
    {
        reader.Read();
        return value;
    }
}
