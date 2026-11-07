namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A binary operator.</summary>
internal enum BinaryOperator
{
    Equal,
    NotEqual,
    StrictEqual,
    StrictNotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LeftShift,
    RightShift,
    UnsignedRightShift,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Exponentiate,
    BitwiseOr,
    BitwiseXor,
    BitwiseAnd,
    In,
    InstanceOf,
}
