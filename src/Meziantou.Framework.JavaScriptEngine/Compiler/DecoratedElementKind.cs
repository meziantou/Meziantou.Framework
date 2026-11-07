namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>The kind of an element of a decorated class (ClassElementDefinition [[Kind]]): the operand of <see cref="OpCode.CreateElementDecoration"/>.</summary>
internal enum DecoratedElementKind : byte
{
    Field,
    Method,
    Getter,
    Setter,
    Accessor,
}
