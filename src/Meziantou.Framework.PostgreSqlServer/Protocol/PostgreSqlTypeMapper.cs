using Meziantou.Framework.PostgreSql.Handler;

namespace Meziantou.Framework.PostgreSql.Protocol;

internal static class PostgreSqlTypeMapper
{
    public static uint GetTypeOid(PostgreSqlColumnType type)
    {
        return type switch
        {
            PostgreSqlColumnType.Boolean => 16,
            PostgreSqlColumnType.Int16 => 21,
            PostgreSqlColumnType.Int32 => 23,
            PostgreSqlColumnType.Int64 => 20,
            PostgreSqlColumnType.Single => 700,
            PostgreSqlColumnType.Double => 701,
            PostgreSqlColumnType.Numeric => 1700,
            PostgreSqlColumnType.Text => 25,
            PostgreSqlColumnType.VarChar => 1043,
            PostgreSqlColumnType.Bytea => 17,
            PostgreSqlColumnType.Uuid => 2950,
            PostgreSqlColumnType.Date => 1082,
            PostgreSqlColumnType.Timestamp => 1114,
            PostgreSqlColumnType.TimestampTz => 1184,
            PostgreSqlColumnType.Json => 114,
            PostgreSqlColumnType.Jsonb => 3802,
            _ => 25,
        };
    }

    public static short GetTypeSize(PostgreSqlColumnType type)
    {
        return type switch
        {
            PostgreSqlColumnType.Boolean => 1,
            PostgreSqlColumnType.Int16 => 2,
            PostgreSqlColumnType.Int32 => 4,
            PostgreSqlColumnType.Int64 => 8,
            PostgreSqlColumnType.Single => 4,
            PostgreSqlColumnType.Double => 8,
            PostgreSqlColumnType.Date => 4,
            PostgreSqlColumnType.Timestamp => 8,
            PostgreSqlColumnType.TimestampTz => 8,
            _ => -1,
        };
    }

    public static PostgreSqlColumnType GetColumnType(uint typeOid)
    {
        return typeOid switch
        {
            16 => PostgreSqlColumnType.Boolean,
            21 => PostgreSqlColumnType.Int16,
            23 => PostgreSqlColumnType.Int32,
            20 => PostgreSqlColumnType.Int64,
            700 => PostgreSqlColumnType.Single,
            701 => PostgreSqlColumnType.Double,
            1700 => PostgreSqlColumnType.Numeric,
            25 => PostgreSqlColumnType.Text,
            1043 => PostgreSqlColumnType.VarChar,
            17 => PostgreSqlColumnType.Bytea,
            2950 => PostgreSqlColumnType.Uuid,
            1082 => PostgreSqlColumnType.Date,
            1114 => PostgreSqlColumnType.Timestamp,
            1184 => PostgreSqlColumnType.TimestampTz,
            114 => PostgreSqlColumnType.Json,
            3802 => PostgreSqlColumnType.Jsonb,
            _ => PostgreSqlColumnType.Text,
        };
    }
}
