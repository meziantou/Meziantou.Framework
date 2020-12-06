using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;

namespace Meziantou.Framework
{
    public sealed class ReflectionDynamicObject : DynamicObject
    {
        private const BindingFlags InstanceDefaultBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticDefaultBindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly ConcurrentDictionary<Type, TypeCache> s_cache = new();

        private readonly object? _originalObject;
        private readonly TypeCache _typeCache;

        public ReflectionDynamicObject(object obj)
        {
            _originalObject = obj ?? throw new ArgumentNullException(nameof(obj));

            var type = obj.GetType();
            _typeCache = s_cache.GetOrAdd(type, t => TypeCache.Create(t));
        }

        public ReflectionDynamicObject(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            _typeCache = s_cache.GetOrAdd(type, TypeCache.Create);
        }

        public ReflectionDynamicObject CreateInstance(params object[] parameters)
        {
            var exceptions = new List<Exception>();

            foreach (var constructor in _typeCache.Constructors)
            {
                var ctorParameters = constructor.GetParameters();
                if (ctorParameters.Length != parameters.Length)
                    continue;

                try
                {
                    return new ReflectionDynamicObject(constructor.Invoke(parameters));
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            Exception? innerException = exceptions.Count == 0 ? null : new AggregateException(exceptions);
            throw new ArgumentException($"Cannot create an instance of {_typeCache.Type.FullName} with the provided parameters.", nameof(parameters), innerException);
        }

        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            return TryGetMemberValue(binder.Name, out result);
        }

        public override bool TrySetMember(SetMemberBinder binder, object? value)
        {
            return TrySetMemberValue(binder.Name, value);
        }

        public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object? result)
        {
            if (_originalObject == null)
            {
                foreach (var indexer in _typeCache.StaticIndexers)
                {
                    if (TryGetIndex(indexer, _originalObject, indexes, out result))
                        return true;
                }
            }
            else
            {
                foreach (var indexer in _typeCache.InstanceIndexers)
                {
                    if (TryGetIndex(indexer, _originalObject, indexes, out result))
                        return true;
                }
            }

            result = null;
            return false;
        }

        public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object? value)
        {
            if (_originalObject == null)
            {
                foreach (var indexer in _typeCache.StaticIndexers)
                {
                    if (TrySetIndex(indexer, instance: null, indexes, value))
                        return true;
                }
            }
            else
            {
                foreach (var indexer in _typeCache.InstanceIndexers)
                {
                    if (TrySetIndex(indexer, _originalObject, indexes, value))
                        return true;
                }
            }

            return false;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
        {
            var type = _typeCache.Type;
            var flags = _originalObject == null ? StaticDefaultBindingFlags : InstanceDefaultBindingFlags;
            flags |= BindingFlags.InvokeMethod;

            while (type != null)
            {
                try
                {
                    result = type.InvokeMember(binder.Name, flags, binder: null, _originalObject, args, culture: null);
                    return true;
                }
                catch (MissingMethodException)
                {
                    type = type.BaseType;
                }
            }

            result = null;
            return false;
        }

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return GetAllMembers().Distinct(StringComparer.Ordinal);

            IEnumerable<string> GetAllMembers()
            {
                foreach (var propertyInfo in _typeCache.InstanceProperties)
                {
                    yield return propertyInfo.Key;
                }

                foreach (var field in _typeCache.InstanceIndexers)
                {
                    yield return field.Name;
                }

                foreach (var field in _typeCache.InstanceFields)
                {
                    yield return field.Key;
                }

                foreach (var propertyInfo in _typeCache.StaticProperties)
                {
                    yield return propertyInfo.Key;
                }

                foreach (var field in _typeCache.StaticIndexers)
                {
                    yield return field.Name;
                }

                foreach (var field in _typeCache.StaticFields)
                {
                    yield return field.Key;
                }
            }
        }

        public override bool TryConvert(ConvertBinder binder, out object? result)
        {
            result = Convert.ChangeType(_originalObject, binder.Type, provider: null);
            return true;
        }

        public override string? ToString()
        {
            if (_originalObject != null)
                return _originalObject.ToString();

            return null;
        }

        private bool TryGetMemberValue(string name, out object? result)
        {
            if (_originalObject == null)
            {
                if (_typeCache.StaticProperties.TryGetValue(name, out var property))
                {
                    result = property.GetValue(null);
                    return true;
                }

                if (_typeCache.StaticFields.TryGetValue(name, out var field))
                {
                    result = field.GetValue(null);
                    return true;
                }
            }
            else
            {
                if (_typeCache.InstanceProperties.TryGetValue(name, out var property))
                {
                    result = property.GetValue(_originalObject);
                    return true;
                }

                if (_typeCache.InstanceFields.TryGetValue(name, out var field))
                {
                    result = field.GetValue(_originalObject);
                    return true;
                }
            }

            result = null;
            return false;
        }

        private bool TrySetMemberValue(string name, object? value)
        {
            if (_originalObject == null)
            {
                if (_typeCache.StaticProperties.TryGetValue(name, out var property))
                {
                    property.SetValue(null, value);
                    return true;
                }

                if (_typeCache.StaticFields.TryGetValue(name, out var field))
                {
                    field.SetValue(null, value);
                    return true;
                }
            }
            else
            {
                if (_typeCache.InstanceProperties.TryGetValue(name, out var property))
                {
                    property.SetValue(_originalObject, value);
                    return true;
                }

                if (_typeCache.InstanceFields.TryGetValue(name, out var field))
                {
                    field.SetValue(_originalObject, value);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetIndex(PropertyInfo indexer, object? instance, object[] indexes, out object? result)
        {
            try
            {
                result = indexer.GetValue(instance, indexes);
                return true;
            }
            catch (ArgumentException)
            {
            }
            catch (TargetParameterCountException)
            {
            }
            catch (TargetInvocationException)
            {
            }

            result = null;
            return false;
        }

        private static bool TrySetIndex(PropertyInfo indexer, object? instance, object[] indexes, object? value)
        {
            try
            {
                indexer.SetValue(instance, value, indexes);
                return true;
            }
            catch (ArgumentException)
            {
            }
            catch (TargetParameterCountException)
            {
            }
            catch (TargetInvocationException)
            {
            }

            return false;
        }

        private sealed class TypeCache
        {
            private TypeCache(Type type)
            {
                Type = type;
            }

            public Type Type { get; }

            public List<ConstructorInfo> Constructors { get; } = new List<ConstructorInfo>();

            public Dictionary<string, PropertyInfo> InstanceProperties { get; } = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            public Dictionary<string, FieldInfo> InstanceFields { get; } = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            public List<PropertyInfo> InstanceIndexers { get; } = new List<PropertyInfo>();

            public Dictionary<string, PropertyInfo> StaticProperties { get; } = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            public Dictionary<string, FieldInfo> StaticFields { get; } = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            public List<PropertyInfo> StaticIndexers { get; } = new List<PropertyInfo>();

            public static TypeCache Create(Type type)
            {
                var typeCache = new TypeCache(type);
                typeCache.Constructors.AddRange(type.GetConstructors());

                var currentType = type;
                while (currentType != null)
                {
                    // Instances
                    foreach (var propertyInfo in currentType.GetProperties(InstanceDefaultBindingFlags))
                    {
                        if (propertyInfo.GetIndexParameters().Any())
                        {
                            typeCache.InstanceIndexers.Add(propertyInfo);
                        }
                        else
                        {
                            if (!typeCache.InstanceProperties.ContainsKey(propertyInfo.Name))
                            {
                                typeCache.InstanceProperties.Add(propertyInfo.Name, propertyInfo);
                            }
                        }
                    }

                    foreach (var fieldInfo in currentType.GetFields(InstanceDefaultBindingFlags))
                    {
                        if (!typeCache.InstanceFields.ContainsKey(fieldInfo.Name))
                        {
                            typeCache.InstanceFields.Add(fieldInfo.Name, fieldInfo);
                        }
                    }

                    // Static
                    foreach (var propertyInfo in currentType.GetProperties(StaticDefaultBindingFlags))
                    {
                        if (propertyInfo.GetIndexParameters().Any())
                        {
                            typeCache.StaticIndexers.Add(propertyInfo);
                        }
                        else
                        {
                            if (!typeCache.StaticProperties.ContainsKey(propertyInfo.Name))
                            {
                                typeCache.StaticProperties.Add(propertyInfo.Name, propertyInfo);
                            }
                        }
                    }

                    foreach (var fieldInfo in currentType.GetFields(StaticDefaultBindingFlags))
                    {
                        if (!typeCache.StaticFields.ContainsKey(fieldInfo.Name))
                        {
                            typeCache.StaticFields.Add(fieldInfo.Name, fieldInfo);
                        }
                    }

                    currentType = currentType.BaseType;
                }

                return typeCache;
            }
        }
    }
}
