#nullable disable
using System;

namespace Meziantou.Framework.CodeDom
{
    public abstract class Expression : CodeObject, ICommentable
    {
        public CommentCollection CommentsAfter { get; }
        public CommentCollection CommentsBefore { get; }

        protected Expression()
        {
            CommentsBefore = new CommentCollection(this, CommentType.InlineComment);
            CommentsAfter = new CommentCollection(this, CommentType.InlineComment);
        }

        public static implicit operator Expression(MemberDeclaration memberDeclaration) => new MemberReferenceExpression(memberDeclaration);

        public static implicit operator Expression(Enum value)
        {
            var type = value.GetType();
            if (Enum.IsDefined(type, value))
            {
                var name = value.ToString();
                return new MemberReferenceExpression(new TypeReference(type), name);
            }
            else
            {
                var underlyingType = Enum.GetUnderlyingType(type);
                var typedValue = Convert.ChangeType(value, underlyingType);
                return new CastExpression(new LiteralExpression(typedValue), new TypeReference(type));
            }
        }

        public static implicit operator Expression(VariableDeclarationStatement variableDeclarationStatement) => new VariableReferenceExpression(variableDeclarationStatement);

        public static implicit operator Expression(MethodArgumentDeclaration argument) => new ArgumentReferenceExpression(argument);

        public static implicit operator Expression(Type type) => new TypeReference(type);

        public static implicit operator Expression(byte value) => new LiteralExpression(value);
        public static implicit operator Expression(sbyte value) => new LiteralExpression(value);
        public static implicit operator Expression(short value) => new LiteralExpression(value);
        public static implicit operator Expression(ushort value) => new LiteralExpression(value);
        public static implicit operator Expression(int value) => new LiteralExpression(value);
        public static implicit operator Expression(uint value) => new LiteralExpression(value);
        public static implicit operator Expression(long value) => new LiteralExpression(value);
        public static implicit operator Expression(ulong value) => new LiteralExpression(value);
        public static implicit operator Expression(float value) => new LiteralExpression(value);
        public static implicit operator Expression(double value) => new LiteralExpression(value);
        public static implicit operator Expression(decimal value) => new LiteralExpression(value);
        public static implicit operator Expression(string value) => new LiteralExpression(value);
        public static implicit operator Expression(bool value) => new LiteralExpression(value);

        public static BinaryExpression operator >(Expression left, Expression right) => new BinaryExpression(BinaryOperator.GreaterThan, left, right);

        public static BinaryExpression operator <(Expression left, Expression right) => new BinaryExpression(BinaryOperator.LessThan, left, right);

        public static BinaryExpression operator >=(Expression left, Expression right) => new BinaryExpression(BinaryOperator.GreaterThanOrEqual, left, right);

        public static BinaryExpression operator <=(Expression left, Expression right) => new BinaryExpression(BinaryOperator.LessThanOrEqual, left, right);

        public static BinaryExpression operator +(Expression left, Expression right) => new BinaryExpression(BinaryOperator.Add, left, right);

        public static BinaryExpression operator -(Expression left, Expression right) => new BinaryExpression(BinaryOperator.Substract, left, right);

        public static BinaryExpression operator *(Expression left, Expression right) => new BinaryExpression(BinaryOperator.Multiply, left, right);

        public static BinaryExpression operator /(Expression left, Expression right) => new BinaryExpression(BinaryOperator.Divide, left, right);

        public static BinaryExpression operator %(Expression left, Expression right) => new BinaryExpression(BinaryOperator.Modulo, left, right);

        public static BinaryExpression operator &(Expression left, Expression right) => new BinaryExpression(BinaryOperator.BitwiseAnd, left, right);

        public static BinaryExpression operator |(Expression left, Expression right) => new BinaryExpression(BinaryOperator.BitwiseOr, left, right);

        public static BinaryExpression operator ^(Expression left, Expression right) => new BinaryExpression(BinaryOperator.Xor, left, right);

        public static BinaryExpression operator <<(Expression left, int right) => new BinaryExpression(BinaryOperator.ShiftLeft, left, new LiteralExpression(right));

        public static BinaryExpression operator >>(Expression left, int right) => new BinaryExpression(BinaryOperator.ShiftRight, left, new LiteralExpression(right));

        public static UnaryExpression operator +(Expression expression) => new UnaryExpression(UnaryOperator.Plus, expression);

        public static UnaryExpression operator -(Expression expression) => new UnaryExpression(UnaryOperator.Minus, expression);

        public static UnaryExpression operator !(Expression expression) => new UnaryExpression(UnaryOperator.Not, expression);

        public static UnaryExpression operator ~(Expression expression) => new UnaryExpression(UnaryOperator.Complement, expression);

        public ArrayIndexerExpression this[params Expression[] indices] => new ArrayIndexerExpression(this, indices);

        // "expr == null" is ambiguous
        //public static bool operator ==(CodeExpression left, object o)
        //{
        //    return Equals(left, o);
        //}

        //public static bool operator !=(CodeExpression left, object o)
        //{
        //    return !Equals(left, o);
        //}

        //public static CodeBinaryExpression operator ==(CodeExpression left, CodeExpression right)
        //{
        //    return new CodeBinaryExpression(BinaryOperator.Equals, left, right);
        //}

        //public static CodeBinaryExpression operator !=(CodeExpression left, CodeExpression right)
        //{
        //    return new CodeBinaryExpression(BinaryOperator.NotEquals, left, right);
        //}

        // Cannot disinguish PreIncrement from PostIncrement
        //public static CodeUnaryExpression operator ++(CodeExpression expression)
        //{
        //    return new CodeUnaryExpression(UnaryOperator.PostIncrement, expression);
        //}

        // Cannot disinguish PreDecrement from PostDecrement
        //public static CodeUnaryExpression operator --(CodeExpression expression)
        //{
        //    return new CodeUnaryExpression(UnaryOperator.PostDecrement, expression);
        //}
    }
}
