using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;

namespace Meziantou.Framework.Utilities
{
    public class ReflectionDynamicObject : DynamicObject
    {
        private const BindingFlags DefaultBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Dictionary<Type, TypeCache> _cache = new Dictionary<Type, TypeCache>();

        private readonly object _originalObject;
        private TypeCache _typeCache;

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
            foreach (var indexer in _typeCache.Indexers)
            {
                try
                {
                    result = indexer.GetValue(_originalObject, indexes);
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
            }

            result = null;
            return false;
        }

        public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object value)
        {
            foreach (var indexer in _typeCache.Indexers)
            {
                try
                {
                    indexer.SetValue(_originalObject, value, indexes);
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
            }

            return false;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            var type = _originalObject.GetType();
            while (type != null)
            {
                try
                {
                    result = type.InvokeMember(binder.Name, DefaultBindingFlags | BindingFlags.InvokeMethod, null, _originalObject, args);
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
            foreach (var propertyInfo in _typeCache.Properties)
            {
                yield return propertyInfo.Key;
            }

            foreach (var field in _typeCache.Indexers)
            {
                yield return field.Name;
            }

            foreach (var field in _typeCache.Fields)
            {
                yield return field.Key;
            }
        }

        public override bool TryConvert(ConvertBinder binder, out object result)
        {
            result = Convert.ChangeType(_originalObject, binder.Type);
            return true;
        }

        public override string ToString()
        {
            return _originalObject.ToString();
        }

        private bool TryGetMemberValue(string name, out object result)
        {
            if (_typeCache.Properties.TryGetValue(name, out PropertyInfo property))
            {
                result = property.GetValue(_originalObject);
                return true;
            }

            if (_typeCache.Fields.TryGetValue(name, out FieldInfo field))
            {
                result = field.GetValue(_originalObject);
                return true;
            }

            result = null;
            return false;
        }

        private bool TrySetMemberValue(string name, object value)
        {
            if (_typeCache.Properties.TryGetValue(name, out PropertyInfo property))
            {
                property.SetValue(_originalObject, value);
                return true;
            }

            if (_typeCache.Fields.TryGetValue(name, out FieldInfo field))
            {
                field.SetValue(_originalObject, value);
                return true;
            }

            return false;
        }

        private class TypeCache
        {
            private TypeCache()
            {
            }

            public Dictionary<string, PropertyInfo> Properties { get; } = new Dictionary<string, PropertyInfo>();
            public Dictionary<string, FieldInfo> Fields { get; } = new Dictionary<string, FieldInfo>();
            public List<PropertyInfo> Indexers { get; } = new List<PropertyInfo>();

            public static TypeCache Create(Type type)
            {
                var instance = new TypeCache();
                while (type != null)
                {
                    foreach (var propertyInfo in type.GetProperties(DefaultBindingFlags))
                    {
                        if (propertyInfo.GetIndexParameters().Any())
                        {
                            instance.Indexers.Add(propertyInfo);
                        }
                        else
                        {
                            if (!instance.Properties.ContainsKey(propertyInfo.Name))
                            {
                                instance.Properties.Add(propertyInfo.Name, propertyInfo);
                            }
                        }
                    }

                    foreach (var fieldInfo in type.GetFields(DefaultBindingFlags))
                    {
                        if (!instance.Fields.ContainsKey(fieldInfo.Name))
                        {
                            instance.Fields.Add(fieldInfo.Name, fieldInfo);
                        }
                    }

                    type = type.BaseType;
                }

                return instance;
            }
        }
    }
}
