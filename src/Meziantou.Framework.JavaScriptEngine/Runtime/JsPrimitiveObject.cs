namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A Boolean, Number, Symbol or BigInt object: [[BooleanData]], [[NumberData]], [[SymbolData]] or [[BigIntData]].</summary>
internal sealed class JsPrimitiveObject(JsObject? prototype, ObjectClass objectClass, JsValue value) : JsObject(prototype, objectClass)
{
    public JsValue PrimitiveValue { get; } = value;
}
