using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Meziantou.Framework.CodeDom
{
    public class TypeReference
    {
        private static readonly char[] s_arityOrArrayCharacters = new[] { '`', '[' };

        private TypeParameter? _typeParameter;
        private TypeDeclaration? _typeDeclaration;
        private string? _typeName;
        private List<TypeReference>? _parameters;
        private List<TypeReference>? _typeDeclarationParameters;

        private TypeReference()
        {
        }

        public TypeReference(TypeDeclaration typeDeclaration)
        {
            _typeDeclaration = typeDeclaration ?? throw new ArgumentNullException(nameof(typeDeclaration));
        }

        public TypeReference(TypeParameter typeParameter)
        {
            _typeParameter = typeParameter ?? throw new ArgumentNullException(nameof(typeParameter));
        }

        public TypeReference(string typeName)
        {
            _typeName = typeName;
        }

        public TypeReference(Type type)
        {
            var name = type.Name;
            var arityOrArrayIndex = name.IndexOfAny(s_arityOrArrayCharacters);
            if (arityOrArrayIndex > 0)
            {
                name = name.Substring(0, arityOrArrayIndex);
            }

            var declaringType = type.DeclaringType;
            if (declaringType != null)
            {
                string? typeName = name;
                string? lastNamespace = null;
                while (declaringType != null)
                {
                    typeName = declaringType.Name + '+' + typeName;

                    lastNamespace = declaringType.Namespace;
                    declaringType = declaringType.DeclaringType;
                }

                _typeName = lastNamespace + '.' + typeName;
            }
            else
            {
                var ns = type.Namespace;
                if (!string.IsNullOrEmpty(ns))
                {
                    _typeName = ns + '.' + name;
                }
                else
                {
                    _typeName = name;
                }
            }

            if (type.IsArray)
            {
                ArrayRank = type.GetArrayRank();
            }

            if (type.IsGenericType)
            {
                foreach (var genericType in type.GenericTypeArguments)
                {
                    Parameters.Add(new TypeReference(genericType));
                }
            }
        }

        public bool IsArray => ArrayRank > 0;

        public int ArrayRank { get; set; }

        public IList<TypeReference> Parameters
        {
            get
            {
                if (_typeDeclaration is IParametrableType typeParameter)
                {
                    if (_typeDeclarationParameters == null)
                    {
                        var collection = new List<TypeReference>();
                        collection.AddRange(typeParameter.Parameters.Select(p => new TypeReference(p.Name ?? throw new InvalidOperationException("TypeReference has no name"))));
                        _typeDeclarationParameters = collection;
                    }

                    return _typeDeclarationParameters;
                }

                if (_parameters == null)
                {
                    _parameters = new List<TypeReference>();
                }

                return _parameters;
            }
        }

        public string? TypeName
        {
            get
            {
                if (_typeParameter != null)
                    return _typeParameter.Name;

                if (_typeDeclaration != null)
                    return _typeDeclaration.Namespace + '.' + _typeDeclaration.Name;

                return _typeName;
            }
        }

        internal string ClrFullTypeNameWithoutArray
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append(TypeName);
                if (Parameters.Any())
                {
                    sb.Append('<');
                    var first = true;
                    foreach (var parameter in Parameters)
                    {
                        if (!first)
                        {
                            sb.Append(", ");
                        }

                        sb.Append(parameter.ClrFullTypeName);

                        first = false;
                    }
                    sb.Append('>');
                }

                return sb.ToString();
            }
        }


        public string ClrFullTypeName
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append(TypeName);
                if (Parameters.Any())
                {
                    sb.Append('<');
                    var first = true;
                    foreach (var parameter in Parameters)
                    {
                        if (!first)
                        {
                            sb.Append(", ");
                        }

                        sb.Append(parameter.ClrFullTypeName);

                        first = false;
                    }
                    sb.Append('>');
                }

                if (IsArray)
                {
                    sb.Append('[');
                    for (var i = 1; i < ArrayRank; i++)
                    {
                        sb.Append(',');
                    }
                    sb.Append(']');
                }

                return sb.ToString();
            }
        }

        public TypeReference Clone()
        {
            var clone = new TypeReference
            {
                _typeName = _typeName,
                _typeDeclaration = _typeDeclaration,
                _typeParameter = _typeParameter,
                ArrayRank = ArrayRank,
            };

            if (_parameters != null)
            {
                clone._parameters = new List<TypeReference>();
                foreach (var parameter in _parameters)
                {
                    clone._parameters.Add(parameter);
                }
            }
            return clone;
        }

        public TypeReference MakeGeneric(params TypeReference[] typeArguments)
        {
            var type = Clone();
            type.Parameters.Clear();
            foreach (var arg in typeArguments)
            {
                type.Parameters.Add(arg);
            }

            return type;
        }

        public TypeReference MakeArray(int rank)
        {
            var type = Clone();
            type.ArrayRank = rank;
            return type;
        }

        public static implicit operator TypeReference(TypeDeclaration typeDeclaration) => new TypeReference(typeDeclaration);

        public static implicit operator TypeReference(Type type) => new TypeReference(type);

        public static implicit operator TypeReference(TypeParameter type) => new TypeReference(type);
    }
}
