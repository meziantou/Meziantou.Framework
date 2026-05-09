using System.Collections;
using System.Globalization;
using System.Reflection;
using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.QueryEngine;

internal static class TdsQueryResultBuilder
{
    public static TdsQueryResult FromValue(object? value)
    {
        if (value is TdsQueryResult result)
        {
            return result;
        }

        if (value is null)
        {
            return FromScalar(value, typeof(object));
        }

        if (IsEnumerable(value, out var rows, out var elementType))
        {
            return FromRows(rows, elementType);
        }

        return IsScalarType(value.GetType()) ? FromScalar(value, value.GetType()) : FromRows([value], value.GetType());
    }

    public static TdsQueryResult FromRows(IReadOnlyList<object?> rows, Type rowType)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(rowType);

        if (IsScalarType(rowType))
        {
            var resultSet = new TdsResultSet();
            resultSet.Columns.Add(new TdsColumn("Value", GetColumnType(rowType), IsNullable(rowType)));
            foreach (var row in rows)
            {
                resultSet.Rows.Add([NormalizeDbNull(row)]);
            }

            return CreateResult(resultSet);
        }

        var members = GetMembers(rowType);
        var set = new TdsResultSet();
        foreach (var member in members)
        {
            set.Columns.Add(new TdsColumn(member.Name, GetColumnType(member.Type), IsNullable(member.Type)));
        }

        foreach (var row in rows)
        {
            var values = new object?[members.Count];
            if (row is not null)
            {
                for (var i = 0; i < members.Count; i++)
                {
                    values[i] = NormalizeDbNull(members[i].GetValue(row));
                }
            }

            set.Rows.Add(values);
        }

        return CreateResult(set);
    }

    private static TdsQueryResult FromScalar(object? value, Type type)
    {
        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Value", GetColumnType(type), isNullable: true));
        resultSet.Rows.Add([NormalizeDbNull(value)]);
        return CreateResult(resultSet);
    }

    private static TdsQueryResult CreateResult(TdsResultSet resultSet)
    {
        var result = new TdsQueryResult();
        result.ResultSets.Add(resultSet);
        return result;
    }

    private static bool IsEnumerable(object value, out IReadOnlyList<object?> rows, out Type elementType)
    {
        if (value is string or byte[])
        {
            rows = [];
            elementType = typeof(object);
            return false;
        }

        if (value is not IEnumerable enumerable)
        {
            rows = [];
            elementType = typeof(object);
            return false;
        }

        rows = enumerable.Cast<object?>().ToArray();
        elementType = GetEnumerableElementType(value.GetType()) ?? rows.FirstOrDefault(row => row is not null)?.GetType() ?? typeof(object);
        return true;
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        return type.GetInterfaces()
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static List<TdsMemberAccessor> GetMembers(Type type)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0 && property.CanRead)
            .Select(property => new TdsMemberAccessor(property.Name, property.PropertyType, property.GetValue))
            .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public).Select(field => new TdsMemberAccessor(field.Name, field.FieldType, field.GetValue)))
            .ToList();
    }

    private static TdsColumnType GetColumnType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
        {
            type = Enum.GetUnderlyingType(type);
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Byte => TdsColumnType.TinyInt,
            TypeCode.Int16 => TdsColumnType.SmallInt,
            TypeCode.Int32 => TdsColumnType.Int32,
            TypeCode.Int64 => TdsColumnType.Int64,
            TypeCode.Boolean => TdsColumnType.Boolean,
            TypeCode.Single => TdsColumnType.Real,
            TypeCode.Double => TdsColumnType.Double,
            TypeCode.Decimal => TdsColumnType.Decimal,
            TypeCode.String => TdsColumnType.NVarChar,
            TypeCode.DateTime => TdsColumnType.DateTime2,
            _ when type == typeof(byte[]) => TdsColumnType.Binary,
            _ when type == typeof(SqlXmlValue) => TdsColumnType.Xml,
            _ when type == typeof(Guid) => TdsColumnType.Guid,
            _ when type == typeof(DateOnly) => TdsColumnType.Date,
            _ when type == typeof(TimeOnly) || type == typeof(TimeSpan) => TdsColumnType.Time,
            _ when type == typeof(DateTimeOffset) => TdsColumnType.DateTimeOffset,
            _ => TdsColumnType.Variant,
        };
    }

    private static bool IsScalarType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(DateTime) ||
            type == typeof(DateOnly) ||
            type == typeof(TimeOnly) ||
            type == typeof(TimeSpan) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(SqlXmlValue) ||
            type == typeof(Guid) ||
            type == typeof(byte[]);
    }

    private static bool IsNullable(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
    }

    private static object? NormalizeDbNull(object? value)
    {
        if (value is SqlXmlValue xml)
        {
            return xml.Value;
        }

        return value is DBNull ? null : value;
    }

    private sealed record TdsMemberAccessor(string Name, Type Type, Func<object, object?> GetValue);
}
