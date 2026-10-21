namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

internal static partial class JsInterpreter
{
    private static readonly PropertyKey LengthKey = new("length");

    /// <summary>GetValue of a property reference whose base is a primitive value.</summary>
    private static JsValue GetPropertyOfPrimitive(JsRealm realm, JsValue target, in PropertyKey key)
    {
        if (target.Cell is JsString str)
        {
            if (key.IsArrayIndex && key.ArrayIndex < (uint)str.Length)
                return JsValue.FromJsString(JsString.Create(str[(int)key.ArrayIndex]));

            if (key == LengthKey)
                return str.Length;
        }

        var prototype = GetPrimitivePrototype(realm, target, key);
        return prototype.Get(key, target);
    }

    private static JsObject GetPrimitivePrototype(JsRealm realm, JsValue target, in PropertyKey key)
    {
        var intrinsics = realm.Intrinsics;
        return target.Kind switch
        {
            JsValueKind.String => intrinsics.StringPrototype,
            JsValueKind.Number => intrinsics.NumberPrototype,
            JsValueKind.Boolean => intrinsics.BooleanPrototype,
            JsValueKind.Symbol => intrinsics.SymbolPrototype,
            JsValueKind.BigInt => intrinsics.BigIntPrototype,
            _ => ThrowHelper.ThrowTypeError<JsObject>($"Cannot read properties of {target} (reading '{key}')"),
        };
    }

    private static JsValue GetElement(JsRealm realm, JsValue target, JsValue keyValue)
    {
        if (target.Cell is JsObject obj)
        {
            // Fast path: an index of the dense elements
            if (keyValue.IsNumber)
                return obj.Get(PropertyKey.FromDouble(keyValue.NumberValue), target);

            return obj.Get(JsOperations.ToPropertyKey(keyValue), target);
        }

        if (target.IsNullOrUndefined)
            return ThrowHelper.ThrowTypeError<JsValue>($"Cannot read properties of {target}{DescribeKeyForError(keyValue, "reading")}");

        var key = keyValue.IsNumber ? PropertyKey.FromDouble(keyValue.NumberValue) : JsOperations.ToPropertyKey(keyValue);
        return GetPropertyOfPrimitive(realm, target, key);
    }

    /// <summary>The key of an element access in an error message: " (reading 'key')". An object key is not converted (ToObject of the base precedes ToPropertyKey: no user code may run).</summary>
    private static string DescribeKeyForError(JsValue keyValue, string verb)
    {
        if (keyValue.IsObject)
            return "";

        return $" ({verb} '{JsOperations.ToPropertyKey(keyValue)}')";
    }

    /// <summary>PutValue of a property reference.</summary>
    private static void SetPropertyOfValue(JsValue target, in PropertyKey key, JsValue value, bool strict)
    {
        if (target.Cell is JsObject obj)
        {
            if (!obj.Set(key, value, target) && strict)
            {
                ThrowHelper.ThrowTypeError($"Cannot assign to read only property '{key}' of object");
            }

            return;
        }

        if (target.IsNullOrUndefined)
        {
            ThrowHelper.ThrowTypeError($"Cannot set properties of {target} (setting '{key}')");
            return;
        }

        // A primitive base: [[Set]] on its wrapper object with the primitive as the receiver (a setter can be called)
        var wrapper = JsOperations.ToObject(target);
        if (!wrapper.Set(key, value, target) && strict)
        {
            ThrowHelper.ThrowTypeError($"Cannot create property '{key}' on {JsOperations.TypeOf(target)} '{target}'");
        }
    }

    private static JsValue DeleteProperty(JsValue target, in PropertyKey key, bool strict)
    {
        var obj = JsOperations.ToObject(target);
        var result = obj.Delete(key);
        if (!result && strict)
        {
            ThrowHelper.ThrowTypeError($"Cannot delete property '{key}' of object");
        }

        return JsValue.FromBoolean(result);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Template objects, regular expressions
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>GetTemplateObject (https://tc39.es/ecma262/#sec-gettemplateobject): one frozen array per site and realm.</summary>
    private static JsValue GetTemplateObject(JsRealm realm, Compiler.TemplateSite site)
    {
        if (realm.TemplateRegistry.TryGetValue(site, out var existing))
            return existing;

        var cooked = new JsValue[site.Cooked.Length];
        var raw = new JsValue[site.Raw.Length];
        for (var i = 0; i < cooked.Length; i++)
        {
            cooked[i] = site.Cooked[i] is { } value ? value : JsValue.Undefined;
            raw[i] = site.Raw[i];
        }

        var template = JsArray.Create(realm, cooked);
        var rawObject = JsArray.Create(realm, raw);
        PropertyOperations.SetIntegrityLevel(rawObject, IntegrityLevel.Frozen);
        template.DefineOwnProperty(new PropertyKey("raw"), PropertyDescriptor.Data(rawObject, PropertyFlags.None));
        PropertyOperations.SetIntegrityLevel(template, IntegrityLevel.Frozen);
        realm.TemplateRegistry.Add(site, template);
        return template;
    }

    private static JsValue CreateRegExp(JsRealm realm, string pattern, string flags) => RegExpBuiltins.RegExpCreate(realm, pattern, flags);
}
