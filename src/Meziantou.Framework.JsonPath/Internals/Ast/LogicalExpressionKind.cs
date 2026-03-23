namespace Meziantou.Framework.Json.Internals;

internal enum LogicalExpressionKind
{
    Or,
    And,
    Not,
    Comparison,
    ExistenceTest,
    FunctionCall,
}
