using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;

namespace Meziantou.Framework.Toml.Tests;

public static class ModelHelper
{
    public static JsonNode ToJson(object? obj)
    {
        switch (obj)
        {
            case TomlArray tomlArray:
            {
                var value = new JsonArray();
                var isLikeTableArray = (tomlArray.Count > 0 && tomlArray[0] is TomlTable);
                foreach (var element in tomlArray)
                {
                    value.Add(ToJson(element));
                }

                return value;
            }
            case bool tomlBoolean:
                return new JsonObject
                {
                    {"type", "bool"},
                    { "value", tomlBoolean.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}
                };
            case TomlDateTime tomlDateTime:
                string kindStr = "";
                switch (tomlDateTime.Kind)
                {
                    case TomlDateTimeKind.OffsetDateTimeByZ:
                    case TomlDateTimeKind.OffsetDateTimeByNumber:
                        kindStr = "datetime";
                        break;
                    case TomlDateTimeKind.LocalDateTime:
                        kindStr = "datetime-local";
                        break;
                    case TomlDateTimeKind.LocalDate:
                        kindStr = "date-local";
                        break;
                    case TomlDateTimeKind.LocalTime:
                        kindStr = "time-local";
                        break;
                }
                return new JsonObject
                {
                    {"type", kindStr},
                    { "value", tomlDateTime.ToString()}
                };
            case double tomlFloat:
                return new JsonObject
                {
                    {"type", "float"},
                    { "value",  tomlFloat == 0.0 ? "0" : TomlFormatHelper.ToString(tomlFloat)}
                };
            case long tomlInteger:
                return new JsonObject
                {
                    {"type", "integer"},
                    { "value", tomlInteger.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}
                };
            case string tomlString:
                return new JsonObject
                {
                    {"type", "string"},
                    { "value", tomlString}
                };
            case TomlTable tomlTable:
            {
                var json = new JsonObject();
                // For the test we order by string key
                foreach (var keyPair in tomlTable.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    json.Add(keyPair.Key, ToJson(keyPair.Value));
                }
                return json;
            }
            case TomlTableArray tomlTableArray:
            {
                var json = new JsonArray();
                foreach (var element in tomlTableArray)
                {
                    json.Add(ToJson(element));
                }
                return json;
            }
        }
        throw new NotSupportedException($"The type element `{obj?.GetType()}` is not supported");
    }
}
