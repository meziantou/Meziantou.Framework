using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed class JsBigInt : JsCell
{
    public static readonly JsBigInt Zero = new(BigInteger.Zero);
    public static readonly JsBigInt One = new(BigInteger.One);

    public JsBigInt(BigInteger value)
        : base(JsValueKind.BigInt)
    {
        Value = value;
    }

    public BigInteger Value { get; }
}
