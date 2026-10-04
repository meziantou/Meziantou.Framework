namespace Meziantou.Framework.NodeJs.Internal;

internal enum JSValueKind
{
    Undefined,
    Null,
    String,
    Boolean,
    BigInt,
    Number,
    NumberLiteral,
    Date,
    Uint8Array,
}
