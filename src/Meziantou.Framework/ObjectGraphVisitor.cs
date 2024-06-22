using System.Collections;
using System.Reflection;

namespace Meziantou.Framework;

public abstract class ObjectGraphVisitor
{
    [RequiresUnreferencedCode("ObjectGraphVisitor uses reflection")]
    public void Visit(object? obj)
    {
        if (obj is null)
            return;

        var hashSet = new HashSet<object>();
        Visit(hashSet, obj);
    }

    [RequiresUnreferencedCode("ObjectGraphVisitor uses reflection")]
    private void Visit(HashSet<object> visitedObjects, object? obj)
    {
        if (obj is null)
            return;

        if (!visitedObjects.Add(obj))
            return;

        VisitValue(obj);

        var type = obj.GetType();
        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!prop.CanRead)
                continue;

            if (prop.GetIndexParameters().Length > 0)
                continue;

            var propValue = prop.GetValue(obj);
            VisitProperty(obj, prop, propValue);
            Visit(visitedObjects, propValue);
        }

        if (obj is not string and IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                Visit(visitedObjects, item);
            }
        }
    }

    protected virtual void VisitProperty(object parentInstance, PropertyInfo property, object? propertyValue)
    {
    }

    protected virtual void VisitValue(object value)
    {
    }
}
