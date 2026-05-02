using System.Globalization;
using System.Linq.Expressions;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Json;

namespace Meziantou.Framework.Tds.QueryEngine;

internal static class SqlFunctions
{
    internal static IDictionary<string, TdsQueryScalarFunction> CreateDefaultScalarFunctions()
    {
        return new Dictionary<string, TdsQueryScalarFunction>(StringComparer.OrdinalIgnoreCase)
        {
            ["UPPER"] = BuildUpperInvariantFunction,
            ["LOWER"] = BuildLowerInvariantFunction,
            ["LEN"] = BuildLenFunction,
            ["CONCAT"] = BuildConcatFunction,
            ["LTRIM"] = BuildLTrimFunction,
            ["RTRIM"] = BuildRTrimFunction,
            ["TRIM"] = BuildTrimFunction,
            ["LEFT"] = BuildLeftFunction,
            ["RIGHT"] = BuildRightFunction,
            ["SUBSTRING"] = BuildSubstringFunction,
            ["REPLACE"] = BuildReplaceFunction,
            ["TRANSLATE"] = BuildTranslateFunction,
            ["STUFF"] = BuildStuffFunction,
            ["STRING_ESCAPE"] = BuildStringEscapeFunction,
            ["FORMAT"] = BuildFormatFunction,
            ["ISNULL"] = BuildIsNullFunction,
            ["CHOOSE"] = BuildChooseFunction,
            ["GETDATE"] = _ => BuildGetDateFunction(),
            ["SYSDATETIME"] = _ => BuildSysDateTimeFunction(),
            ["EOMONTH"] = BuildEoMonthFunction,
            ["YEAR"] = BuildYearFunction,
            ["MONTH"] = BuildMonthFunction,
            ["DAY"] = BuildDayFunction,
            ["ABS"] = BuildAbsFunction,
            ["ROUND"] = BuildRoundFunction,
            ["CEILING"] = BuildCeilingFunction,
            ["FLOOR"] = BuildFloorFunction,
            ["POWER"] = BuildPowerFunction,
            ["SQRT"] = BuildSqrtFunction,
            ["EXP"] = BuildExpFunction,
            ["LOG"] = BuildLogFunction,
            ["SIN"] = BuildSinFunction,
            ["COS"] = BuildCosFunction,
            ["TAN"] = BuildTanFunction,
            ["ASIN"] = BuildAsinFunction,
            ["ACOS"] = BuildAcosFunction,
            ["ATAN"] = BuildAtanFunction,
            ["ATN2"] = BuildAtn2Function,
            ["COT"] = BuildCotFunction,
            ["ISJSON"] = BuildIsJsonFunction,
            ["JSON_VALUE"] = BuildJsonValueFunction,
            ["JSON_PATH_EXISTS"] = BuildJsonPathExistsFunction,
            ["JSON_QUERY"] = BuildJsonQueryFunction,
        };
    }

    private static Expression BuildUpperInvariantFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "UPPER");
        return Expression.Call(EnsureString(arguments[0]), typeof(string).GetMethod(nameof(string.ToUpperInvariant), Type.EmptyTypes)!);
    }

    private static Expression BuildLowerInvariantFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "LOWER");
        return Expression.Call(EnsureString(arguments[0]), typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!);
    }

    private static Expression BuildLenFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "LEN");
        return Expression.Property(EnsureString(arguments[0]), nameof(string.Length));
    }

    private static Expression BuildConcatFunction(IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count == 0)
        {
            throw new TdsQueryEngineException("CONCAT requires at least one argument.");
        }

        if (arguments.Count == 1)
        {
            return EnsureString(arguments[0]);
        }

        var stringArguments = arguments.Select(EnsureString);
        return Expression.Call(typeof(string).GetMethod(nameof(string.Concat), [typeof(string[])])!, Expression.NewArrayInit(typeof(string), stringArguments));
    }

    private static Expression BuildLTrimFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "LTRIM");
        return Expression.Call(EnsureString(arguments[0]), typeof(string).GetMethod(nameof(string.TrimStart), Type.EmptyTypes)!);
    }

    private static Expression BuildRTrimFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "RTRIM");
        return Expression.Call(EnsureString(arguments[0]), typeof(string).GetMethod(nameof(string.TrimEnd), Type.EmptyTypes)!);
    }

    private static Expression BuildTrimFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "TRIM");
        return Expression.Call(EnsureString(arguments[0]), typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!);
    }

    private static Expression BuildLeftFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 2, "LEFT");
        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(SubstringCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureString(arguments[0]),
            Expression.Constant(0),
            EnsureInt(arguments[1]));
    }

    private static Expression BuildRightFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 2, "RIGHT");
        var value = EnsureString(arguments[0]);
        var length = EnsureInt(arguments[1]);
        var start = Expression.Subtract(Expression.Property(value, nameof(string.Length)), length);

        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(SubstringCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            value,
            start,
            length);
    }

    private static Expression BuildSubstringFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 3, "SUBSTRING");
        var start = Expression.Subtract(EnsureInt(arguments[1]), Expression.Constant(1));
        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(SubstringCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureString(arguments[0]),
            start,
            EnsureInt(arguments[2]));
    }

    private static Expression BuildReplaceFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 3, "REPLACE");
        return Expression.Call(
            EnsureString(arguments[0]),
            typeof(string).GetMethod(nameof(string.Replace), [typeof(string), typeof(string)])!,
            EnsureString(arguments[1]),
            EnsureString(arguments[2]));
    }

    private static Expression BuildTranslateFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 3, "TRANSLATE");
        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(TranslateCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureString(arguments[0]),
            EnsureString(arguments[1]),
            EnsureString(arguments[2]));
    }

    private static Expression BuildStuffFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 4, "STUFF");
        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(StuffCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureString(arguments[0]),
            EnsureInt(arguments[1]),
            EnsureInt(arguments[2]),
            EnsureString(arguments[3]));
    }

    private static Expression BuildStringEscapeFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 2, "STRING_ESCAPE");
        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(StringEscapeCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureString(arguments[0]),
            EnsureString(arguments[1]));
    }

    private static Expression BuildFormatFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 2, "FORMAT");
        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(FormatCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            Expression.Convert(arguments[0], typeof(object)),
            EnsureString(arguments[1]));
    }

    private static Expression BuildIsNullFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 2, "ISNULL");
        var left = EnsureNullable(arguments[0]);
        var right = ConvertExpression(EnsureNullable(arguments[1]), left.Type);
        return Expression.Coalesce(left, right);
    }

    private static Expression BuildChooseFunction(IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count < 2)
        {
            throw new TdsQueryEngineException("CHOOSE requires an index and at least one choice.");
        }

        var index = EnsureInt(arguments[0]);
        var choices = arguments.Skip(1).Select(EnsureNullable).ToArray();
        var choiceType = choices[0].Type;
        for (var i = 1; i < choices.Length; i++)
        {
            if (choices[i].Type != choiceType)
            {
                choices[i] = ConvertExpression(choices[i], choiceType);
            }
        }

        Expression result = Expression.Constant(null, choiceType);
        for (var i = choices.Length - 1; i >= 0; i--)
        {
            result = Expression.Condition(
                Expression.Equal(index, Expression.Constant(i + 1)),
                choices[i],
                result);
        }

        return result;
    }

    private static Expression BuildGetDateFunction()
    {
        return Expression.Property(null, typeof(DateTime).GetProperty(nameof(DateTime.UtcNow))!);
    }

    private static Expression BuildSysDateTimeFunction()
    {
        return BuildGetDateFunction();
    }

    private static Expression BuildEoMonthFunction(IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count is < 1 or > 2)
        {
            throw new TdsQueryEngineException("EOMONTH requires one date argument and an optional month offset.");
        }

        var offset = arguments.Count == 2 ? EnsureInt(arguments[1]) : Expression.Constant(0);
        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(EoMonthCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureDateTime(arguments[0]),
            offset);
    }

    private static Expression BuildYearFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "YEAR");
        return Expression.Property(EnsureDateTime(arguments[0]), nameof(DateTime.Year));
    }

    private static Expression BuildMonthFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "MONTH");
        return Expression.Property(EnsureDateTime(arguments[0]), nameof(DateTime.Month));
    }

    private static Expression BuildDayFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "DAY");
        return Expression.Property(EnsureDateTime(arguments[0]), nameof(DateTime.Day));
    }

    private static Expression BuildAbsFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "ABS");
        var value = arguments[0];
        var type = Nullable.GetUnderlyingType(value.Type) ?? value.Type;
        var method = type switch
        {
            _ when type == typeof(decimal) => typeof(decimal).GetMethod(nameof(decimal.Abs), [typeof(decimal)]),
            _ when type == typeof(double) => typeof(Math).GetMethod(nameof(Math.Abs), [typeof(double)]),
            _ when type == typeof(float) => typeof(Math).GetMethod(nameof(Math.Abs), [typeof(float)]),
            _ when type == typeof(long) => typeof(Math).GetMethod(nameof(Math.Abs), [typeof(long)]),
            _ when type == typeof(int) => typeof(Math).GetMethod(nameof(Math.Abs), [typeof(int)]),
            _ when type == typeof(short) => typeof(Math).GetMethod(nameof(Math.Abs), [typeof(short)]),
            _ when type == typeof(sbyte) => typeof(Math).GetMethod(nameof(Math.Abs), [typeof(sbyte)]),
            _ => null,
        };
        if (method is null)
        {
            throw new TdsQueryEngineException($"ABS does not support type '{type.Name}'.");
        }

        return Expression.Call(method, ConvertExpression(value, type));
    }

    private static Expression BuildRoundFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 2, "ROUND");
        return Expression.Call(
            typeof(Math).GetMethod(nameof(Math.Round), [typeof(double), typeof(int)])!,
            EnsureDouble(arguments[0]),
            EnsureInt(arguments[1]));
    }

    private static Expression BuildCeilingFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "CEILING");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Ceiling), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildFloorFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "FLOOR");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Floor), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildPowerFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 2, "POWER");
        return Expression.Call(
            typeof(Math).GetMethod(nameof(Math.Pow), [typeof(double), typeof(double)])!,
            EnsureDouble(arguments[0]),
            EnsureDouble(arguments[1]));
    }

    private static Expression BuildSqrtFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "SQRT");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Sqrt), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildExpFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "EXP");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Exp), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildLogFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "LOG");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Log), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildSinFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "SIN");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Sin), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildCosFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "COS");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Cos), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildTanFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "TAN");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Tan), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildAsinFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "ASIN");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Asin), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildAcosFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "ACOS");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Acos), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildAtanFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "ATAN");
        return Expression.Call(typeof(Math).GetMethod(nameof(Math.Atan), [typeof(double)])!, EnsureDouble(arguments[0]));
    }

    private static Expression BuildAtn2Function(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 2, "ATN2");
        return Expression.Call(
            typeof(Math).GetMethod(nameof(Math.Atan2), [typeof(double), typeof(double)])!,
            EnsureDouble(arguments[0]),
            EnsureDouble(arguments[1]));
    }

    private static Expression BuildCotFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "COT");
        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(CotCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureDouble(arguments[0]));
    }

    private static Expression BuildIsJsonFunction(IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count is < 1 or > 2)
        {
            throw new TdsQueryEngineException("ISJSON requires one or two arguments.");
        }

        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(IsJsonCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureString(arguments[0]),
            arguments.Count == 2 ? EnsureString(arguments[1]) : Expression.Constant(null, typeof(string)));
    }

    private static Expression BuildJsonValueFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 2, "JSON_VALUE");
        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(JsonValueCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureString(arguments[0]),
            EnsureString(arguments[1]));
    }

    private static Expression BuildJsonPathExistsFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 2, "JSON_PATH_EXISTS");
        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(JsonPathExistsCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureString(arguments[0]),
            EnsureString(arguments[1]));
    }

    private static Expression BuildJsonQueryFunction(IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count is < 1 or > 2)
        {
            throw new TdsQueryEngineException("JSON_QUERY requires one or two arguments.");
        }

        return Expression.Call(
            typeof(SqlFunctions).GetMethod(nameof(JsonQueryCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            EnsureString(arguments[0]),
            arguments.Count == 2 ? EnsureString(arguments[1]) : Expression.Constant("$"));
    }

    private static void ValidateArgCount(IReadOnlyList<Expression> arguments, int expectedCount, string functionName)
    {
        if (arguments.Count != expectedCount)
        {
            throw new TdsQueryEngineException($"{functionName} requires exactly {expectedCount.ToString(CultureInfo.InvariantCulture)} argument.");
        }
    }

    private static Expression ConvertExpression(Expression expression, Type targetType)
    {
        if (expression.Type == targetType)
        {
            return expression;
        }

        var conversionMethod = typeof(SqlFunctions).GetMethod(nameof(ConvertToTypeCore), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var converted = Expression.Call(
            conversionMethod,
            Expression.Convert(expression, typeof(object)),
            Expression.Constant(targetType, typeof(Type)));
        return Expression.Convert(converted, targetType);
    }

    private static Expression EnsureNullable(Expression expression)
    {
        if (!expression.Type.IsValueType || Nullable.GetUnderlyingType(expression.Type) is not null)
        {
            return expression;
        }

        return Expression.Convert(expression, typeof(Nullable<>).MakeGenericType(expression.Type));
    }

    private static Expression EnsureInt(Expression expression) => ConvertExpression(expression, typeof(int));

    private static Expression EnsureDouble(Expression expression) => ConvertExpression(expression, typeof(double));

    private static Expression EnsureDateTime(Expression expression) => ConvertExpression(expression, typeof(DateTime));

    private static Expression EnsureString(Expression expression)
    {
        if (expression.Type == typeof(string))
        {
            return expression;
        }

        return Expression.Call(
            typeof(Convert).GetMethod(nameof(Convert.ToString), [typeof(object), typeof(IFormatProvider)])!,
            Expression.Convert(expression, typeof(object)),
            Expression.Constant(CultureInfo.InvariantCulture));
    }

    private static object? ConvertToTypeCore(object? value, Type targetType)
    {
        if (value is DBNull)
        {
            value = null;
        }

        if (value is null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
            {
                throw new TdsQueryEngineException($"Cannot convert NULL to '{targetType.Name}'.");
            }

            return null;
        }

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (targetType == typeof(DateTime))
        {
            return value is DateTime dateTime ? dateTime : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(DateTimeOffset))
        {
            return value is DateTimeOffset dateTimeOffset ? dateTimeOffset : DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(DateOnly))
        {
            return value is DateOnly dateOnly ? dateOnly : DateOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(TimeSpan))
        {
            return value is TimeSpan timeSpan ? timeSpan : TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(Guid))
        {
            return value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static string? LeftCore(string? value, int length)
    {
        if (value is null)
        {
            return null;
        }

        if (length <= 0)
        {
            return string.Empty;
        }

        return length >= value.Length ? value : value[..length];
    }

    private static string? RightCore(string? value, int length)
    {
        if (value is null)
        {
            return null;
        }

        if (length <= 0)
        {
            return string.Empty;
        }

        return length >= value.Length ? value : value[(value.Length - length)..];
    }

    private static string? SubstringCore(string? value, int start, int length)
    {
        if (value is null)
        {
            return null;
        }

        if (length <= 0)
        {
            return string.Empty;
        }

        var startIndex = Math.Max(start, 0);
        if (startIndex >= value.Length)
        {
            return string.Empty;
        }

        var maxLength = Math.Min(length, value.Length - startIndex);
        return maxLength <= 0 ? string.Empty : value.Substring(startIndex, maxLength);
    }

    private static string? TranslateCore(string? value, string? sourceCharacters, string? targetCharacters)
    {
        if (value is null || sourceCharacters is null || targetCharacters is null)
        {
            return null;
        }

        if (sourceCharacters.Length != targetCharacters.Length)
        {
            throw new TdsQueryEngineException("TRANSLATE requires source and target arguments with the same length.");
        }

        var mapping = new Dictionary<char, char>(sourceCharacters.Length);
        for (var i = 0; i < sourceCharacters.Length; i++)
        {
            mapping[sourceCharacters[i]] = targetCharacters[i];
        }

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (mapping.TryGetValue(chars[i], out var replacement))
            {
                chars[i] = replacement;
            }
        }

        return new string(chars);
    }

    private static string? StuffCore(string? value, int start, int length, string? replacement)
    {
        if (value is null || replacement is null)
        {
            return null;
        }

        if (start <= 0 || length < 0 || start > value.Length)
        {
            return null;
        }

        var startIndex = start - 1;
        var removeLength = Math.Min(length, value.Length - startIndex);
        return value.Remove(startIndex, removeLength).Insert(startIndex, replacement);
    }

    private static string? StringEscapeCore(string? value, string? escapeType)
    {
        if (value is null)
        {
            return null;
        }

        if (!string.Equals(escapeType, "json", StringComparison.OrdinalIgnoreCase))
        {
            throw new TdsQueryEngineException("STRING_ESCAPE currently supports only 'json'.");
        }

        return JavaScriptEncoder.Default.Encode(value);
    }

    private static string? FormatCore(object? value, string? format)
    {
        if (value is null)
        {
            return null;
        }

        return value is IFormattable formattable
            ? formattable.ToString(format, CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static DateTime EoMonthCore(DateTime dateValue, int monthOffset)
    {
        var target = dateValue.AddMonths(monthOffset);
        var days = DateTime.DaysInMonth(target.Year, target.Month);
        return new DateTime(target.Year, target.Month, days, 0, 0, 0, target.Kind);
    }

    private static double CotCore(double value)
    {
        return 1.0d / Math.Tan(value);
    }

    private static int? IsJsonCore(string? json, string? jsonTypeConstraint)
    {
        if (json is null)
        {
            return null;
        }

        if (!TryParseJson(json, out var document))
        {
            return 0;
        }

        using (document)
        {
            var rootKind = document.RootElement.ValueKind;
            return jsonTypeConstraint switch
            {
                null => 1,
                var value when value.Equals("VALUE", StringComparison.OrdinalIgnoreCase) => rootKind != JsonValueKind.Undefined ? 1 : 0,
                var value when value.Equals("ARRAY", StringComparison.OrdinalIgnoreCase) => rootKind == JsonValueKind.Array ? 1 : 0,
                var value when value.Equals("OBJECT", StringComparison.OrdinalIgnoreCase) => rootKind == JsonValueKind.Object ? 1 : 0,
                var value when value.Equals("SCALAR", StringComparison.OrdinalIgnoreCase) => rootKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? 1 : 0,
                _ => throw new TdsQueryEngineException("ISJSON supports only VALUE, ARRAY, OBJECT, or SCALAR as the second argument."),
            };
        }
    }

    private static int? JsonPathExistsCore(string? json, string? path)
    {
        if (json is null || path is null)
        {
            return null;
        }

        var node = ParseJsonNode(json, "JSON_PATH_EXISTS");
        var (jsonPath, mode) = ParseJsonPath(path, "JSON_PATH_EXISTS");
        try
        {
            return jsonPath.Evaluate(node, mode).Count > 0 ? 1 : 0;
        }
        catch (JsonPathEvaluationException ex)
        {
            throw new TdsQueryEngineException($"JSON_PATH_EXISTS failed to evaluate path '{path}': {ex.Message}");
        }
    }

    private static string? JsonValueCore(string? json, string? path)
    {
        if (json is null || path is null)
        {
            return null;
        }

        var node = ParseJsonNode(json, "JSON_VALUE");
        var (jsonPath, mode) = ParseJsonPath(path, "JSON_VALUE");
        JsonPathResult result;
        try
        {
            result = jsonPath.Evaluate(node, mode);
        }
        catch (JsonPathEvaluationException ex)
        {
            throw new TdsQueryEngineException($"JSON_VALUE failed to evaluate path '{path}': {ex.Message}");
        }

        if (result.Count == 0)
        {
            if (mode == JsonPathEvaluationMode.Strict)
            {
                throw new TdsQueryEngineException($"JSON_VALUE path '{path}' did not match any value.");
            }

            return null;
        }

        return JsonNodeToSqlScalarString(result[0].Value, "JSON_VALUE", path, mode);
    }

    private static string? JsonQueryCore(string? json, string? path)
    {
        if (json is null || path is null)
        {
            return null;
        }

        var node = ParseJsonNode(json, "JSON_QUERY");
        var (jsonPath, mode) = ParseJsonPath(path, "JSON_QUERY");
        JsonPathResult result;
        try
        {
            result = jsonPath.Evaluate(node, mode);
        }
        catch (JsonPathEvaluationException ex)
        {
            throw new TdsQueryEngineException($"JSON_QUERY failed to evaluate path '{path}': {ex.Message}");
        }

        if (result.Count == 0)
        {
            if (mode == JsonPathEvaluationMode.Strict)
            {
                throw new TdsQueryEngineException($"JSON_QUERY path '{path}' did not match any value.");
            }

            return null;
        }

        var matchedValue = result[0].Value;
        if (matchedValue is JsonObject or JsonArray)
        {
            return matchedValue.ToJsonString();
        }

        if (mode == JsonPathEvaluationMode.Strict)
        {
            throw new TdsQueryEngineException($"JSON_QUERY path '{path}' resolved to a scalar value.");
        }

        return null;
    }

    private static (JsonPath JsonPath, JsonPathEvaluationMode Mode) ParseJsonPath(string path, string functionName)
    {
        var trimmedPath = path.Trim();
        var mode = JsonPathEvaluationMode.Lax;
        if (trimmedPath.StartsWith("strict ", StringComparison.OrdinalIgnoreCase))
        {
            mode = JsonPathEvaluationMode.Strict;
            trimmedPath = trimmedPath[7..].TrimStart();
        }
        else if (trimmedPath.StartsWith("lax ", StringComparison.OrdinalIgnoreCase))
        {
            trimmedPath = trimmedPath[4..].TrimStart();
        }

        if (trimmedPath.Length == 0)
        {
            throw new TdsQueryEngineException($"{functionName} requires a non-empty JSON path.");
        }

        try
        {
            return (JsonPath.Parse(trimmedPath), mode);
        }
        catch (FormatException ex)
        {
            throw new TdsQueryEngineException($"{functionName} path '{path}' is not a valid JSON path: {ex.Message}");
        }
    }

    private static JsonNode ParseJsonNode(string json, string functionName)
    {
        try
        {
            return JsonNode.Parse(json) ?? throw new TdsQueryEngineException($"{functionName} does not support null JSON roots.");
        }
        catch (JsonException ex)
        {
            throw new TdsQueryEngineException($"{functionName} input is not a valid JSON document: {ex.Message}");
        }
    }

    private static string? JsonNodeToSqlScalarString(JsonNode? node, string functionName, string path, JsonPathEvaluationMode mode)
    {
        if (node is null)
        {
            return null;
        }

        var element = JsonSerializer.SerializeToElement(node);
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
            _ => mode == JsonPathEvaluationMode.Strict
                ? throw new TdsQueryEngineException($"{functionName} path '{path}' resolved to a non-scalar value.")
                : null,
        };
    }

    private static bool TryParseJson(string json, [NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }
}
