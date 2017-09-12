using System;
using System.Linq;
using System.Text;

namespace Meziantou.Framework.CodeDom
{
    public class CodeTypeReference : CodeExpression
    {
        private CodeTypeDeclaration _typeDeclaration;
        private string _name;
        private string _namespace;
        private CodeObjectCollection<CodeTypeReference> _parameters;

        public CodeTypeReference()
        {
        }

        public CodeTypeReference(CodeTypeDeclaration typeDeclaration)
        {
            _typeDeclaration = typeDeclaration;
        }

        public CodeTypeReference(string clrFullTypeName)
        {
            var parsedType = ParsedType.Parse(clrFullTypeName);
            FromParsedType(parsedType);
        }

        private void FromParsedType(ParsedType parsedType)
        {
            Namespace = parsedType.Namespace;
            Name = parsedType.Name;
            if (parsedType.Arguments != null)
            {
                foreach (var argument in parsedType.Arguments)
                {
                    CodeTypeReference typeReference = new CodeTypeReference();
                    typeReference.FromParsedType(argument);
                    Parameters.Add(typeReference);
                }
            }
        }

        public CodeTypeReference(Type type) : this(type.FullName)
        {
        }

        public string Name
        {
            get
            {
                if (_typeDeclaration != null)
                    return _typeDeclaration.Name;

                return _name;
            }
            set
            {
                _name = value;
                _typeDeclaration = null;
            }
        }

        public string Namespace
        {
            get
            {
                if (_typeDeclaration != null)
                    return _typeDeclaration.Namespace;

                return _namespace;
            }
            set
            {
                _namespace = value;
                _typeDeclaration = null;
            }
        }

        public CodeObjectCollection<CodeTypeReference> Parameters
        {
            get
            {
                if (_typeDeclaration is ITypeParameters typeParameter)
                {
                    return typeParameter.Parameters;
                }

                if (_parameters == null)
                {
                    _parameters = new CodeObjectCollection<CodeTypeReference>(this);
                }

                return _parameters;
            }
        }

        public string ClrFullTypeName
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                if (!string.IsNullOrEmpty(Namespace))
                {
                    sb.Append(Namespace + ".");
                }

                sb.Append(Name);
                if (Parameters.Any())
                {
                    sb.Append('<');
                    bool first = true;
                    foreach (var parameter in Parameters)
                    {
                        if (!first)
                            sb.Append(", ");
                        sb.Append(parameter.ClrFullTypeName);

                        first = false;
                    }
                    sb.Append('>');
                }

                return sb.ToString();
            }
        }

        public CodeTypeReference Clone()
        {
            CodeTypeReference clone = new CodeTypeReference();
            clone._name = _name;
            clone._namespace = _namespace;
            clone._typeDeclaration = _typeDeclaration;
            if (_parameters != null)
            {
                clone._parameters = new CodeObjectCollection<CodeTypeReference>(clone);
                foreach (var parameter in _parameters)
                {
                    clone._parameters.Add(parameter);
                }
            }
            return clone;
        }

        public static implicit operator CodeTypeReference(CodeTypeDeclaration typeDeclaration)
        {
            return new CodeTypeReference(typeDeclaration);
        }

        public static implicit operator CodeTypeReference(Type type)
        {
            return new CodeTypeReference(type);
        }
    }
}