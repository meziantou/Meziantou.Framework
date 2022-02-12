﻿namespace Meziantou.Framework.CodeDom
{
    public class MethodInvokeArgumentExpression : Expression
    {
        private Expression? _value;

        public MethodInvokeArgumentExpression()
        {
        }

        public MethodInvokeArgumentExpression(string? name, Expression? value)
        {
            Name = name;
            Value = value;
        }

        public MethodInvokeArgumentExpression(string? name, Expression? value, Direction direction)
        {
            Name = name;
            Value = value;
            Direction = direction;
        }

        public MethodInvokeArgumentExpression(Expression? value)
        {
            Value = value;
        }

        public MethodInvokeArgumentExpression(Expression? value, Direction direction)
        {
            Value = value;
            Direction = direction;
        }

        public string? Name { get; set; }
        public Direction Direction { get; set; }

        public Expression? Value
        {
            get => _value;
            set => SetParent(ref _value, value);
        }

        public static implicit operator MethodInvokeArgumentExpression(MethodArgumentDeclaration argument) => new(new ArgumentReferenceExpression(argument));

        public static implicit operator MethodInvokeArgumentExpression(VariableDeclarationStatement variable) => new(new VariableReferenceExpression(variable));
    }
}
