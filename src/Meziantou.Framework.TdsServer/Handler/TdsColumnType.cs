using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents a column type used for TDS serialization.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names mirror SQL/TDS primitive types.")]
public enum TdsColumnType
{
    TinyInt,
    SmallInt,
    Int32,
    Int64,
    Boolean,
    Real,
    Double,
    Decimal,
    Money,
    SmallMoney,
    NVarChar,
    Binary,
    Guid,
    Date,
    Time,
    DateTime,
    DateTime2,
    DateTimeOffset,
    Xml,
    Json,
    Variant,
    UserDefined,
    Table,
}
