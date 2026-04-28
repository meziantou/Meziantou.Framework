using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Represents a PostgreSQL column type used for wire serialization.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names mirror PostgreSQL primitive types.")]
public enum PostgreSqlColumnType
{
    Boolean,
    Int16,
    Int32,
    Int64,
    Single,
    Double,
    Numeric,
    Text,
    VarChar,
    Bytea,
    Uuid,
    Date,
    Timestamp,
    TimestampTz,
    Json,
    Jsonb,
}
