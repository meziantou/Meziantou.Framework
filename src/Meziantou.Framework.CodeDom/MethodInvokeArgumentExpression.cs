namespace Meziantou.Framework.CodeDom
{
    public class MethodInvokeArgumentExpression : Expression
    {
        private Expression _value;

        public MethodInvokeArgumentExpression()
        {
        }

        public MethodInvokeArgumentExpression(string name, Expression value)
        {
            Name = name;
            Value = value;
        }

        public MethodInvokeArgumentExpression(Expression value)
        {
            Value = value;
        }

        public string Name { get; set; }

        public Expression Value
        {
            get => _value;
            set => SetParent(ref _value, value);
        }

        public static implicit operator MethodInvokeArgumentExpression(MethodArgumentDeclaration argument)
        {
            return new MethodInvokeArgumentExpression(new ArgumentReferenceExpression(argument));
        }

        public static implicit operator MethodInvokeArgumentExpression(VariableDeclarationStatement variable)
        {
            return new MethodInvokeArgumentExpression(new VariableReference(variable));
        }
    }
}