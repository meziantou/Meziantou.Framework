namespace Meziantou.Framework.CodeDom
{
    public class CodeMethodInvokeArgumentExpression : CodeExpression
    {
        private CodeExpression _value;

        public CodeMethodInvokeArgumentExpression()
        {
        }

        public CodeMethodInvokeArgumentExpression(string name, CodeExpression value)
        {
            Name = name;
            Value = value;
        }

        public CodeMethodInvokeArgumentExpression(CodeExpression value)
        {
            Value = value;
        }

        public string Name { get; set; }

        public CodeExpression Value
        {
            get { return _value; }
            set
            {
                _value = SetParent(value);
            }
        }
        
        public static implicit operator CodeMethodInvokeArgumentExpression(CodeMethodArgumentDeclaration argument)
        {
            return new CodeMethodInvokeArgumentExpression(new CodeArgumentReferenceExpression(argument));
        }

        public static implicit operator CodeMethodInvokeArgumentExpression(CodeVariableDeclarationStatement variable)
        {
            return new CodeMethodInvokeArgumentExpression(new CodeVariableReference(variable));
        }
    }
}