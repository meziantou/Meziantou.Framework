using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

public abstract class ObjectGraphVisitor
{
    private static readonly ConditionalWeakTable<Type, PropertyInfo[]> PropertiesCache = new();

    [RequiresUnreferencedCode("ObjectGraphVisitor uses reflection")]
    public void Visit(object? obj)
    {
        if (obj is null)
            return;

        // Reference identity is what breaks cycles. The default comparer uses the objects' own
        // Equals/GetHashCode, which makes distinct but equal instances look already visited.
        var hashSet = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(hashSet, valueTypePath: [], obj);
    }

    [RequiresUnreferencedCode("ObjectGraphVisitor uses reflection")]
    private void Visit(HashSet<object> visitedObjects, List<object> valueTypePath, object? obj)
    {
        if (obj is null)
            return;

        var type = obj.GetType();
        var isValueType = type.IsValueType;
        if (isValueType)
        {
            // Value types are boxed into a new object every time they are read, so the reference set cannot detect a
            // repeat. Stop when the same value is already being visited, such as a property that returns its own instance.
            if (valueTypePath.Contains(obj))
                return;
        }
        else if (!visitedObjects.Add(obj))
        {
            return;
        }

        VisitValue(obj);

        if (IsLeafType(type))
            return;

        if (isValueType)
        {
            valueTypePath.Add(obj);
        }

        foreach (var prop in GetReadableProperties(type))
        {
            var propValue = prop.GetValue(obj);
            VisitProperty(obj, prop, propValue);
            Visit(visitedObjects, valueTypePath, propValue);
        }

        if (obj is not string and IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                Visit(visitedObjects, valueTypePath, item);
            }
        }

        if (isValueType)
        {
            valueTypePath.RemoveAt(valueTypePath.Count - 1);
        }
    }

    // Values whose properties only describe the value itself, such as DateTime.Date that returns another DateTime
    private static bool IsLeafType(Type type)
    {
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(Guid);
    }

    [RequiresUnreferencedCode("ObjectGraphVisitor uses reflection")]
    private static PropertyInfo[] GetReadableProperties(Type type)
    {
        return PropertiesCache.GetValue(type, visitedType => visitedType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(prop => prop.CanRead && prop.GetIndexParameters().Length is 0)
            .ToArray());
    }

    protected virtual void VisitProperty(object parentInstance, PropertyInfo property, object? propertyValue)
    {
    }

    protected virtual void VisitValue(object value)
    {
    }
}
