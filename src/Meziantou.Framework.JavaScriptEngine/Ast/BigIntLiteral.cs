using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A BigInt literal (<c>123n</c>).</summary>
internal sealed class BigIntLiteral : Node
{
    public BigInteger Value { get; set; }

    public override NodeType Type => NodeType.BigIntLiteral;
}
