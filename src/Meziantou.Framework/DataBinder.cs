using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Meziantou.Framework;

// https://referencesource.microsoft.com/#System.Web/UI/DataBinder.cs,bc4362a9cfc4c370,references
public static class DataBinder
{
    private static readonly char[] ExpressionPartSeparator = new char[] { '.' };
    private static readonly char[] IndexExprStartChars = new char[] { '[', '(' };
    private static readonly char[] IndexExprEndChars = new char[] { ']', ')' };
    private static readonly ConcurrentDictionary<Type, PropertyDescriptorCollection> PropertyCache = new();

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    public static object? Eval(object container, string expression!!)
    {
        expression = expression.Trim();
        if (expression.Length == 0)
        {
            throw new ArgumentException("expression is empty or whitespaces", nameof(expression));
        }

        if (container == null)
        {
            return null;
        }

        var expressionParts = expression.Split(ExpressionPartSeparator);
        return Eval(container, expressionParts);
    }

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    private static object? Eval(object container, string[] expressionParts)
    {
        object? prop;
        int i;

        for (prop = container, i = 0; (i < expressionParts.Length) && (prop != null); i++)
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

        if ((value == null) || (value == DBNull.Value))
        {
            return string.Empty;
        }
        else
        {
            if (string.IsNullOrEmpty(format))
            {
                return value.ToString() ?? string.Empty;
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
    public static object? GetPropertyValue(object container!!, string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentNullException(nameof(propertyName));

        var pd = GetPropertiesFromCache(container).Find(propertyName, ignoreCase: true);
        if (pd != null)
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
        if (value == null || value == DBNull.Value)
        {
            return string.Empty;
        }
        else
        {
            if (string.IsNullOrEmpty(format))
            {
                return value.ToString() ?? string.Empty;
            }
            else
            {
                return string.Format(provider: null, format, value);
            }
        }
    }

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    public static object? GetIndexedPropertyValue(object container!!, string expression)
    {
        if (string.IsNullOrEmpty(expression))
            throw new ArgumentNullException(nameof(expression));

        var intIndex = false;

        var indexExprStart = expression.IndexOfAny(IndexExprStartChars);
        var indexExprEnd = expression.IndexOfAny(IndexExprEndChars, indexExprStart + 1);

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
            if (((index[0] == '"') && (index[^1] == '"')) ||
                ((index[0] == '\'') && (index[^1] == '\'')))
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

        if (indexValue == null)
            throw new ArgumentException($"Databinding: '{expression}' is not a valid indexed expression.", nameof(expression));

        object? collectionProp;
        if ((propName != null) && (propName.Length != 0))
        {
            collectionProp = GetPropertyValue(container, propName);
        }
        else
        {
            collectionProp = container;
        }

        if (collectionProp != null)
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
                var propInfo = collectionProp.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.Instance, binder: null, returnType: null, new Type[] { indexValue.GetType() }, modifiers: null);
                if (propInfo != null)
                    return propInfo.GetValue(collectionProp, new object[] { indexValue });

                throw new ArgumentException($"Databinding: '{collectionProp.GetType().FullName}' does not allow indexed access.", nameof(expression));
            }
        }

        return null;
    }

    [RequiresUnreferencedCode("TypeDescriptor use reflection")]
    public static string? GetIndexedPropertyValue(object container, string propertyName, string format)
    {
        var value = GetIndexedPropertyValue(container, propertyName);
        if (value == null || Convert.IsDBNull(value))
        {
            return string.Empty;
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
