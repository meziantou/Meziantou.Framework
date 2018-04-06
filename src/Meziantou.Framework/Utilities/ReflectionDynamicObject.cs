using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;

namespace Meziantou.Framework.Utilities
{
    public class ReflectionDynamicObject : DynamicObject
    {
        private const BindingFlags InstanceDefaultBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticDefaultBindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<Type, TypeCache> _cache = new Dictionary<Type, TypeCache>();

        private readonly object _originalObject;
        private readonly TypeCache _typeCache;

        public ReflectionDynamicObject(object obj)
        {
            _originalObject = obj ?? throw new ArgumentNullException(nameof(obj));

            var type = obj.GetType();
            if (!_cache.TryGetValue(type, out var typeCache))
            {
                typeCache = TypeCache.Create(type);
                _cache[type] = typeCache;
            }

            _typeCache = typeCache;
        }

        public ReflectionDynamicObject(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (!_cache.TryGetValue(type, out var typeCache))
            {
                typeCache = TypeCache.Create(type);
                _cache[type] = typeCache;
            }

            _typeCache = typeCache;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            return TryGetMemberValue(binder.Name, out result);
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            return TrySetMemberValue(binder.Name, value);
        }

        public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
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

        public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object value)
        {
            if (_originalObject == null)
            {
                foreach (var indexer in _typeCache.StaticIndexers)
                {
                    if (TrySetIndex(indexer, null, indexes, value))
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

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            var type = _typeCache.Type;
            var flags = _originalObject == null ? StaticDefaultBindingFlags : InstanceDefaultBindingFlags;
            flags |= BindingFlags.InvokeMethod;

            while (type != null)
            {
                try
                {
                    result = type.InvokeMember(binder.Name, flags, null, _originalObject, args);
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
            return GetAllMembers().Distinct();

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

        public override bool TryConvert(ConvertBinder binder, out object result)
        {
            result = Convert.ChangeType(_originalObject, binder.Type);
            return true;
        }

        public override string ToString()
        {
            if (_originalObject != null)
                return _originalObject.ToString();

            return null;
        }

        private bool TryGetMemberValue(string name, out object result)
        {
            if (_originalObject == null)
            {
                if (_typeCache.StaticProperties.TryGetValue(name, out PropertyInfo property))
                {
                    result = property.GetValue(null);
                    return true;
                }

                if (_typeCache.StaticFields.TryGetValue(name, out FieldInfo field))
                {
                    result = field.GetValue(null);
                    return true;
                }
            }
            else
            {
                if (_typeCache.InstanceProperties.TryGetValue(name, out PropertyInfo property))
                {
                    result = property.GetValue(_originalObject);
                    return true;
                }

                if (_typeCache.InstanceFields.TryGetValue(name, out FieldInfo field))
                {
                    result = field.GetValue(_originalObject);
                    return true;
                }
            }

            result = null;
            return false;
        }

        private bool TrySetMemberValue(string name, object value)
        {
            if (_originalObject == null)
            {
                if (_typeCache.StaticProperties.TryGetValue(name, out PropertyInfo property))
                {
                    property.SetValue(null, value);
                    return true;
                }

                if (_typeCache.StaticFields.TryGetValue(name, out FieldInfo field))
                {
                    field.SetValue(null, value);
                    return true;
                }
            }
            else
            {
                if (_typeCache.InstanceProperties.TryGetValue(name, out PropertyInfo property))
                {
                    property.SetValue(_originalObject, value);
                    return true;
                }

                if (_typeCache.InstanceFields.TryGetValue(name, out FieldInfo field))
                {
                    field.SetValue(_originalObject, value);
                    return true;
                }
            }

            return false;
        }

        private bool TryGetIndex(PropertyInfo indexer, object instance, object[] indexes, out object result)
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

        private bool TrySetIndex(PropertyInfo indexer, object instance, object[] indexes, object value)
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

        private class TypeCache
        {
            private TypeCache(Type type)
            {
                Type = type;
            }

            public Type Type { get; }

            public Dictionary<string, PropertyInfo> InstanceProperties { get; } = new Dictionary<string, PropertyInfo>();
            public Dictionary<string, FieldInfo> InstanceFields { get; } = new Dictionary<string, FieldInfo>();
            public List<PropertyInfo> InstanceIndexers { get; } = new List<PropertyInfo>();

            public Dictionary<string, PropertyInfo> StaticProperties { get; } = new Dictionary<string, PropertyInfo>();
            public Dictionary<string, FieldInfo> StaticFields { get; } = new Dictionary<string, FieldInfo>();
            public List<PropertyInfo> StaticIndexers { get; } = new List<PropertyInfo>();

            public static TypeCache Create(Type type)
            {
                var instance = new TypeCache(type);
                while (type != null)
                {
                    // Instances
                    foreach (var propertyInfo in type.GetProperties(InstanceDefaultBindingFlags))
                    {
                        if (propertyInfo.GetIndexParameters().Any())
                        {
                            instance.InstanceIndexers.Add(propertyInfo);
                        }
                        else
                        {
                            if (!instance.InstanceProperties.ContainsKey(propertyInfo.Name))
                            {
                                instance.InstanceProperties.Add(propertyInfo.Name, propertyInfo);
                            }
                        }
                    }

                    foreach (var fieldInfo in type.GetFields(InstanceDefaultBindingFlags))
                    {
                        if (!instance.InstanceFields.ContainsKey(fieldInfo.Name))
                        {
                            instance.InstanceFields.Add(fieldInfo.Name, fieldInfo);
                        }
                    }

                    // Static
                    foreach (var propertyInfo in type.GetProperties(StaticDefaultBindingFlags))
                    {
                        if (propertyInfo.GetIndexParameters().Any())
                        {
                            instance.StaticIndexers.Add(propertyInfo);
                        }
                        else
                        {
                            if (!instance.StaticProperties.ContainsKey(propertyInfo.Name))
                            {
                                instance.StaticProperties.Add(propertyInfo.Name, propertyInfo);
                            }
                        }
                    }

                    foreach (var fieldInfo in type.GetFields(StaticDefaultBindingFlags))
                    {
                        if (!instance.StaticFields.ContainsKey(fieldInfo.Name))
                        {
                            instance.StaticFields.Add(fieldInfo.Name, fieldInfo);
                        }
                    }

                    type = type.BaseType;
                }

                return instance;
            }
        }
    }
}
