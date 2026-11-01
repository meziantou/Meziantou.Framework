using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace Meziantou.Framework;

// https://referencesource.microsoft.com/#System.Web/UI/DataBinder.cs,bc4362a9cfc4c370,references
public static class DataBinder
{
    private static readonly char[] IndexExprStartChars = ['[', '('];
    private static readonly ConcurrentDictionary<Type, PropertyDescriptorCollection> PropertyCache = new();

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    public static object? Eval(object? container, string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        expression = expression.Trim();
        if (expression.Length is 0)
        {
            throw new ArgumentException("expression is empty or whitespaces", nameof(expression));
        }

        if (container is null)
        {
            return null;
        }

        var expressionParts = SplitExpression(expression);
        return Eval(container, expressionParts);
    }

    // Splits the expression at the dots, except the ones inside an index, such as Items["a.b"] or Values[1.5]
    private static string[] SplitExpression(string expression)
    {
        var parts = new List<string>();
        var partStart = 0;
        var position = 0;
        while (position < expression.Length)
        {
            var c = expression[position];
            if (c is '[' or '(')
            {
                var indexEnd = FindIndexEnd(expression, position + 1);
                if (indexEnd < 0)
                    break;

                position = indexEnd + 1;
                continue;
            }

            if (c == '.')
            {
                parts.Add(expression[partStart..position]);
                partStart = position + 1;
            }

            position++;
        }

        parts.Add(expression[partStart..]);
        return [.. parts];
    }

    // Returns the position of the character that closes the index starting at indexStart, or -1.
    // An index that starts with a quote ends after the matching quote, so it can contain any character.
    private static int FindIndexEnd(string expression, int indexStart)
    {
        var position = indexStart;
        while (position < expression.Length && char.IsWhiteSpace(expression[position]))
        {
            position++;
        }

        if (position < expression.Length && expression[position] is '"' or '\'')
        {
            var closingQuote = expression.IndexOf(expression[position], position + 1, StringComparison.Ordinal);
            if (closingQuote < 0)
                return -1;

            position = closingQuote + 1;
        }

        var end = expression.AsSpan(position).IndexOfAny(']', ')');
        return end < 0 ? -1 : position + end;
    }

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    private static object? Eval(object? container, string[] expressionParts)
    {
        object? prop;
        int i;

        for (prop = container, i = 0; (i < expressionParts.Length) && (prop is not null); i++)
        {
            var expr = expressionParts[i];
            var indexedExpr = expr.IndexOfAny(IndexExprStartChars) >= 0;

            if (!indexedExpr)
            {
                prop = GetPropertyValue(prop, expr);
            }
            else
            {
                prop = GetIndexedPropertyValue(prop, expr);
            }
        }

        return prop;
    }

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    public static string Eval(object container, string expression, string format)
    {
        var value = Eval(container, expression);

        if ((value is null) || (value == DBNull.Value))
        {
            return "";
        }
        else
        {
            if (string.IsNullOrEmpty(format))
            {
                return value.ToString() ?? "";
            }
            else
            {
                return string.Format(provider: null, format, value);
            }
        }
    }

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    private static PropertyDescriptorCollection GetPropertiesFromCache(object container)
    {
        // We don't cache if the object implements ICustomTypeDescriptor.
        if (container is not ICustomTypeDescriptor)
        {
            var containerType = container.GetType();
            if (!PropertyCache.TryGetValue(containerType, out var properties))
            {
                properties = TypeDescriptor.GetProperties(containerType);
                PropertyCache.TryAdd(containerType, properties);
            }

            return properties;
        }

        return TypeDescriptor.GetProperties(container);
    }

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    public static object? GetPropertyValue(object container, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(container);

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentNullException(nameof(propertyName));

        var pd = GetPropertiesFromCache(container).Find(propertyName, ignoreCase: true);
        if (pd is not null)
        {
            return pd.GetValue(container);
        }
        else
        {
            throw new ArgumentException($"Databinding: '{container.GetType().FullName}' does not contain a property with the name '{propertyName}'", nameof(propertyName));
        }
    }

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    public static string GetPropertyValue(object container, string propertyName, string format)
    {
        var value = GetPropertyValue(container, propertyName);
        if (value is null || value == DBNull.Value)
        {
            return "";
        }
        else
        {
            if (string.IsNullOrEmpty(format))
            {
                return value.ToString() ?? "";
            }
            else
            {
                return string.Format(provider: null, format, value);
            }
        }
    }

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    public static object? GetIndexedPropertyValue(object container, string expression)
    {
        ArgumentNullException.ThrowIfNull(container);

        if (string.IsNullOrEmpty(expression))
            throw new ArgumentNullException(nameof(expression));

        var intIndex = false;

        var indexExprStart = expression.IndexOfAny(IndexExprStartChars);
        var indexExprEnd = indexExprStart < 0 ? -1 : FindIndexEnd(expression, indexExprStart + 1);

        if ((indexExprStart < 0) || (indexExprEnd < 0) || (indexExprEnd == indexExprStart + 1))
            throw new ArgumentException($"Databinding: '{expression}' is not a valid indexed expression.", nameof(expression));

        string? propName = null;
        object? indexValue = null;
        var index = expression.Substring(indexExprStart + 1, indexExprEnd - indexExprStart - 1).Trim();

        if (indexExprStart != 0)
        {
            propName = expression[..indexExprStart];
        }

        if (index.Length != 0)
        {
            if (index.Length >= 2 &&
                (((index[0] == '"') && (index[^1] == '"')) ||
                 ((index[0] == '\'') && (index[^1] == '\''))))
            {
                indexValue = index[1..^1];
            }
            else
            {
                if (char.IsDigit(index[0]))
                {
                    // treat it as a number
                    intIndex = int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex);
                    if (intIndex)
                    {
                        indexValue = parsedIndex;
                    }
                    else
                    {
                        indexValue = index;
                    }
                }
                else
                {
                    // treat as a string
                    indexValue = index;
                }
            }
        }

        if (indexValue is null)
            throw new ArgumentException($"Databinding: '{expression}' is not a valid indexed expression.", nameof(expression));

        object? collectionProp;
        if ((propName is not null) && (propName.Length != 0))
        {
            collectionProp = GetPropertyValue(container, propName);
        }
        else
        {
            collectionProp = container;
        }

        if (collectionProp is not null)
        {
            if (collectionProp is Array arrayProp && intIndex)
            {
                return arrayProp.GetValue((int)indexValue);
            }
            else if ((collectionProp is IList list) && intIndex)
            {
                return list[(int)indexValue];
            }
            else
            {
                var propInfo = collectionProp.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.Instance, binder: null, returnType: null, [indexValue.GetType()], modifiers: null);
                if (propInfo != null)
                    return propInfo.GetValue(collectionProp, [indexValue]);

                throw new ArgumentException($"Databinding: '{collectionProp.GetType().FullName}' does not allow indexed access.", nameof(expression));
            }
        }

        return null;
    }

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    public static string? GetIndexedPropertyValue(object container, string propertyName, string format)
    {
        var value = GetIndexedPropertyValue(container, propertyName);
        if (value is null || Convert.IsDBNull(value))
        {
            return "";
        }
        else
        {
            if (string.IsNullOrEmpty(format))
            {
                return value.ToString();
            }
            else
            {
                return string.Format(provider: null, format, value);
            }
        }
    }
}
