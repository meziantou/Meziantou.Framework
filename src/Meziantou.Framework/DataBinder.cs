using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Meziantou.Framework
{
    // https://referencesource.microsoft.com/#System.Web/UI/DataBinder.cs,bc4362a9cfc4c370,references
    public static class DataBinder
    {
        private static readonly char[] s_expressionPartSeparator = new char[] { '.' };
        private static readonly char[] s_indexExprStartChars = new char[] { '[', '(' };
        private static readonly char[] s_indexExprEndChars = new char[] { ']', ')' };
        private static readonly ConcurrentDictionary<Type, PropertyDescriptorCollection> s_propertyCache = new();

        public static object? Eval(object container, string expression)
        {
            if (expression == null)
            {
                throw new ArgumentNullException(nameof(expression));
            }

            expression = expression.Trim();
            if (expression.Length == 0)
            {
                throw new ArgumentException("expression is empty or whitespaces", nameof(expression));
            }

            if (container == null)
            {
                return null;
            }

            var expressionParts = expression.Split(s_expressionPartSeparator);
            return Eval(container, expressionParts);
        }

        private static object? Eval(object container, string[] expressionParts)
        {
            object? prop;
            int i;

            for (prop = container, i = 0; (i < expressionParts.Length) && (prop != null); i++)
            {
                var expr = expressionParts[i];
                var indexedExpr = expr.IndexOfAny(s_indexExprStartChars) >= 0;

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

        private static PropertyDescriptorCollection GetPropertiesFromCache(object container)
        {
            // We don't cache if the object implements ICustomTypeDescriptor.
            if (container is not ICustomTypeDescriptor)
            {
                var containerType = container.GetType();
                if (!s_propertyCache.TryGetValue(containerType, out var properties))
                {
                    properties = TypeDescriptor.GetProperties(containerType);
                    s_propertyCache.TryAdd(containerType, properties);
                }
                return properties;
            }

            return TypeDescriptor.GetProperties(container);
        }

        public static object? GetPropertyValue(object container, string propertyName)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

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

        public static object? GetIndexedPropertyValue(object container, string expression)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            if (string.IsNullOrEmpty(expression))
                throw new ArgumentNullException(nameof(expression));

            var intIndex = false;

            var indexExprStart = expression.IndexOfAny(s_indexExprStartChars);
            var indexExprEnd = expression.IndexOfAny(s_indexExprEndChars, indexExprStart + 1);

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
}
