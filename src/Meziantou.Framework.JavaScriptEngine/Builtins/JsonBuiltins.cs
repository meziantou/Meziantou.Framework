namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The JSON object (https://tc39.es/ecma262/#sec-json-object) and JSON.parse source text access (https://tc39.es/proposal-json-parse-with-source/).</summary>
internal static class JsonBuiltins
{
    private static readonly PropertyKey SourceKey = new("source");

    private static readonly BuiltinMethodTable JsonMethods =
    [
        new("isRawJSON", 1, IsRawJson),
        new("parse", 2, Parse),
        new("rawJSON", 1, RawJson),
        new("stringify", 3, Stringify),
    ];

    public static JsObject Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var json = new JsObject(intrinsics.ObjectPrototype);
        realm.DefineMethods(json, JsonMethods);
        json.DefineBuiltIn(WellKnownSymbols.ToStringTag, "JSON", PropertyFlags.Configurable);
        intrinsics.AddGlobal("JSON", json);
        return json;
    }

    /// <summary>JSON.parse(text, reviver)</summary>
    private static JsValue Parse(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var jsonString = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        var reviver = arguments.Length > 1 ? arguments[1] : default;
        var realm = function.Realm;
        if (reviver.Cell is not JsObject { IsCallable: true } reviverFunction)
            return JsonParser.Parse(realm, jsonString, trackSource: false, out _);

        var unfiltered = JsonParser.Parse(realm, jsonString, trackSource: true, out var snapshot);
        var root = new JsObject(realm.Intrinsics.ObjectPrototype);
        var rootName = new PropertyKey("");
        root.CreateDataPropertyOrThrow(rootName, unfiltered);
        var reviverState = new ReviverState(realm, jsonString, reviverFunction);
        return reviverState.InternalizeProperty(root, rootName, snapshot);
    }

    /// <summary>JSON.stringify(value, replacer, space)</summary>
    private static JsValue Stringify(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var value = arguments.Length > 0 ? arguments[0] : default;
        var replacer = arguments.Length > 1 ? arguments[1] : default;
        var space = arguments.Length > 2 ? arguments[2] : default;

        JsObject? replacerFunction = null;
        List<PropertyKey>? propertyList = null;
        if (replacer.Cell is JsObject replacerObject)
        {
            if (replacerObject.IsCallable)
            {
                replacerFunction = replacerObject;
            }
            else if (JsOperations.IsArray(replacer))
            {
                propertyList = [];
                var names = new HashSet<string>(StringComparer.Ordinal);
                var length = JsOperations.LengthOfArrayLike(replacerObject);
                for (long k = 0; k < length; k++)
                {
                    var v = replacerObject.Get(PropertyKey.FromInteger(k));
                    string? item = null;
                    if (v.Cell is JsString str)
                    {
                        item = str.ToString();
                    }
                    else if (v.IsNumber)
                    {
                        item = NumberFormatting.ToString(v.NumberValue);
                    }
                    else if (v.Cell is JsObject { Class: ObjectClass.String or ObjectClass.Number })
                    {
                        item = JsOperations.ToString(v);
                    }

                    if (item is not null && names.Add(item))
                    {
                        propertyList.Add(new PropertyKey(item));
                    }
                }
            }
        }

        if (space.Cell is JsObject spaceObject)
        {
            if (spaceObject.Class == ObjectClass.Number)
            {
                space = JsOperations.ToNumber(space);
            }
            else if (spaceObject.Class == ObjectClass.String)
            {
                space = JsValue.FromJsString(JsOperations.ToJsString(space));
            }
        }

        string gap;
        if (space.IsNumber)
        {
            var spaceMV = Math.Min(10, JsOperations.ToIntegerOrInfinity(space));
            gap = spaceMV < 1 ? "" : new string(' ', (int)spaceMV);
        }
        else if (space.Cell is JsString spaceString)
        {
            var s = spaceString.ToString();
            gap = s.Length <= 10 ? s : s[..10];
        }
        else
        {
            gap = "";
        }

        var realm = function.Realm;
        var wrapper = new JsObject(realm.Intrinsics.ObjectPrototype);
        wrapper.CreateDataPropertyOrThrow(new PropertyKey(""), value);
        var serializer = new JsonSerializer(realm.Engine, replacerFunction, propertyList, gap);
        var result = serializer.Serialize(wrapper);
        return result is null ? JsValue.Undefined : result;
    }

    /// <summary>JSON.rawJSON(text)</summary>
    private static JsValue RawJson(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var jsonString = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        if (jsonString.Length == 0 || IsJsonWhiteSpace(jsonString[0]) || IsJsonWhiteSpace(jsonString[^1]))
        {
            ThrowHelper.ThrowSyntaxError("Invalid value for JSON.rawJSON");
        }

        JsonParser.ValidatePrimitive(function.Realm, jsonString);
        return new JsRawJsonObject(jsonString);
    }

    private static bool IsJsonWhiteSpace(char c) => c is '\t' or '\n' or '\r' or ' ';

    /// <summary>JSON.isRawJSON(O)</summary>
    private static JsValue IsRawJson(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return arguments.Length > 0 && arguments[0].Cell is JsRawJsonObject;
    }

    /// <summary>EnumerableOwnProperties(O, key): the own enumerable string keys, in the order of [[OwnPropertyKeys]].</summary>
    internal static List<PropertyKey> EnumerableOwnPropertyKeys(JsObject obj)
    {
        var keys = obj.OwnPropertyKeys();
        var result = new List<PropertyKey>(keys.Count);
        foreach (var key in keys)
        {
            if (key.IsSymbol)
                continue;

            if (obj.GetOwnProperty(key, out var descriptor) && descriptor.Enumerable)
            {
                result.Add(key);
            }
        }

        return result;
    }

    /// <summary>The state of InternalizeJSONProperty.</summary>
    private sealed class ReviverState(JsRealm realm, string source, JsObject reviver)
    {
        /// <summary>InternalizeJSONProperty(holder, name, reviver, parseRecord)</summary>
        public JsValue InternalizeProperty(JsObject holder, in PropertyKey name, JsonParseRecord? parseRecord)
        {
            realm.Engine.CheckCallDepth();
            var value = holder.Get(name);
            var context = new JsObject(realm.Intrinsics.ObjectPrototype);
            List<JsonParseRecord>? elementRecords = null;
            Dictionary<PropertyKey, JsonParseRecord>? entryRecords = null;
            if (parseRecord is not null && JsOperations.SameValue(parseRecord.Value, value))
            {
                if (!value.IsObject)
                {
                    context.CreateDataPropertyOrThrow(SourceKey, source.Substring(parseRecord.SourceStart, parseRecord.SourceLength));
                }

                elementRecords = parseRecord.Elements;
                entryRecords = parseRecord.Entries;
            }

            if (value.Cell is JsObject obj)
            {
                if (JsOperations.IsArray(value))
                {
                    var elementCount = elementRecords?.Count ?? 0;
                    var length = JsOperations.LengthOfArrayLike(obj);
                    for (long i = 0; i < length; i++)
                    {
                        var property = PropertyKey.FromInteger(i);
                        var elementRecord = i < elementCount ? elementRecords![(int)i] : null;
                        var newElement = InternalizeProperty(obj, property, elementRecord);
                        if (newElement.IsUndefined)
                        {
                            obj.Delete(property);
                        }
                        else
                        {
                            obj.CreateDataProperty(property, newElement);
                        }
                    }
                }
                else
                {
                    foreach (var key in EnumerableOwnPropertyKeys(obj))
                    {
                        JsonParseRecord? entryRecord = null;
                        entryRecords?.TryGetValue(key, out entryRecord);
                        var newElement = InternalizeProperty(obj, key, entryRecord);
                        if (newElement.IsUndefined)
                        {
                            obj.Delete(key);
                        }
                        else
                        {
                            obj.CreateDataProperty(key, newElement);
                        }
                    }
                }
            }

            return reviver.Call(holder, [name.ToJsValue(), value, context]);
        }
    }
}
