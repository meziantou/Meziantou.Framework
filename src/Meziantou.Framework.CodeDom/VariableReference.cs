namespace Meziantou.Framework.CodeDom
{
    public class VariableReference : Expression
    {
        private VariableDeclarationStatement _variableDeclarationStatement;
        private string _name;

        public VariableReference()
        {
        }

        public VariableReference(VariableDeclarationStatement variableDeclarationStatement)
        {
            _variableDeclarationStatement = variableDeclarationStatement;
        }

        public VariableReference(string name)
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
        
        public static implicit operator VariableReference(VariableDeclarationStatement variableDeclarationStatement)
        {
            return new VariableReference(variableDeclarationStatement);
        }
    }
}