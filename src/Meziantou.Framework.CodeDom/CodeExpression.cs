using System;

namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeExpression : CodeObject, ICommentable
    {
        public CodeCommentCollection CommentsAfter { get; }
        public CodeCommentCollection CommentsBefore { get; }

        public CodeExpression()
        {
            CommentsBefore = new CodeCommentCollection(this, CodeCommentType.InlineComment);
            CommentsAfter = new CodeCommentCollection(this, CodeCommentType.InlineComment);
        }

        public static implicit operator CodeExpression(CodeMemberDeclaration memberDeclaration)
        {
            return new CodeMemberReferenceExpression(memberDeclaration);
        }

        public static implicit operator CodeExpression(Enum value)
        {
            var type = value.GetType();
            if (Enum.IsDefined(type, value))
            {
                var name = value.ToString();
                return new CodeMemberReferenceExpression(new CodeTypeReference(type), name);
            }
            else
            {
                var underlyingType = Enum.GetUnderlyingType(type);
                object typedValue = Convert.ChangeType(value, underlyingType); ;
                return new CodeCastExpression(new CodeLiteralExpression(typedValue), new CodeTypeReference(type));
            }
        }

        public static implicit operator CodeExpression(CodeVariableDeclarationStatement variableDeclarationStatement)
        {
            return new CodeVariableReference(variableDeclarationStatement);
        }

        public static implicit operator CodeExpression(CodeMethodArgumentDeclaration argument)
        {
            return new CodeArgumentReferenceExpression(argument);
        }

        public static implicit operator CodeExpression(Type type) => new CodeTypeReference(type);

        public static implicit operator CodeExpression(byte value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(sbyte value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(short value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(ushort value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(int value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(uint value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(long value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(ulong value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(float value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(double value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(decimal value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(string value) => new CodeLiteralExpression(value);
        public static implicit operator CodeExpression(bool value) => new CodeLiteralExpression(value);
        
        public static CodeBinaryExpression operator >(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.GreaterThan, left, right);
        }

        public static CodeBinaryExpression operator <(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.LessThan, left, right);
        }

        public static CodeBinaryExpression operator >=(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.GreaterThanOrEqual, left, right);
        }

        public static CodeBinaryExpression operator <=(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.LessThanOrEqual, left, right);
        }

        public static CodeBinaryExpression operator +(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.Add, left, right);
        }

        public static CodeBinaryExpression operator -(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.Substract, left, right);
        }

        public static CodeBinaryExpression operator *(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.Multiply, left, right);
        }

        public static CodeBinaryExpression operator /(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.Divide, left, right);
        }

        public static CodeBinaryExpression operator %(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.Modulo, left, right);
        }

        public static CodeBinaryExpression operator &(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.BitwiseAnd, left, right);
        }

        public static CodeBinaryExpression operator |(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.BitwiseOr, left, right);
        }

        public static CodeBinaryExpression operator ^(CodeExpression left, CodeExpression right)
        {
            return new CodeBinaryExpression(BinaryOperator.Xor, left, right);
        }

        public static CodeBinaryExpression operator <<(CodeExpression left, int right)
        {
            return new CodeBinaryExpression(BinaryOperator.ShiftLeft, left, new CodeLiteralExpression(right));
        }

        public static CodeBinaryExpression operator >>(CodeExpression left, int right)
        {
            return new CodeBinaryExpression(BinaryOperator.ShiftRight, left, new CodeLiteralExpression(right));
        }

        public static CodeUnaryExpression operator +(CodeExpression expression)
        {
            return new CodeUnaryExpression(UnaryOperator.Plus, expression);
        }

        public static CodeUnaryExpression operator -(CodeExpression expression)
        {
            return new CodeUnaryExpression(UnaryOperator.Minus, expression);
        }

        public static CodeUnaryExpression operator !(CodeExpression expression)
        {
            return new CodeUnaryExpression(UnaryOperator.Not, expression);
        }

        public static CodeUnaryExpression operator ~(CodeExpression expression)
        {
            return new CodeUnaryExpression(UnaryOperator.Complement, expression);
        }

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