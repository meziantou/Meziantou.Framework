namespace Meziantou.Framework.CodeDom
{
    public class CodeArgumentReferenceExpression : CodeExpression
    {
        private CodeMethodArgumentDeclaration _argumentDeclaration;
        private string _name;

        public CodeArgumentReferenceExpression()
        {
        }

        public CodeArgumentReferenceExpression(CodeMethodArgumentDeclaration argumentDeclaration)
        {
            _argumentDeclaration = argumentDeclaration;
        }

        public CodeArgumentReferenceExpression(string name)
        {
            Name = name;
        }

        public string Name
        {
            get
            {
                if (_argumentDeclaration != null)
                    return _argumentDeclaration.Name;

                return _name;
            }
            set
            {
                _name = value;
                _argumentDeclaration = null;
            }
        }

        public static implicit operator CodeArgumentReferenceExpression(CodeMethodArgumentDeclaration methodArgumentDeclaration)
        {
            return new CodeArgumentReferenceExpression(methodArgumentDeclaration);
        }
    }
}