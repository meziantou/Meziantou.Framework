namespace Meziantou.Framework.Json.Internals;

internal enum FunctionArgumentKind
{
    Literal,
    FilterQuery,
    LogicalExpression,
    FunctionCall,
}
