namespace Meziantou.Framework.CodeDom
{
    public class CodeVariableReference : CodeExpression
    {
        private CodeVariableDeclarationStatement _variableDeclarationStatement;
        private string _name;

        public CodeVariableReference()
        {
        }

        public CodeVariableReference(CodeVariableDeclarationStatement variableDeclarationStatement)
        {
            _variableDeclarationStatement = variableDeclarationStatement;
        }

        public CodeVariableReference(string name)
        {
            Name = name;
        }

        public string Name
        {
            get
            {
                if (_variableDeclarationStatement != null)
                    return _variableDeclarationStatement.Name;

                return _name;
            }
            set
            {
                _name = value;
                _variableDeclarationStatement = null;
            }
        }
        
        public static implicit operator CodeVariableReference(CodeVariableDeclarationStatement variableDeclarationStatement)
        {
            return new CodeVariableReference(variableDeclarationStatement);
        }
    }
}