using System;
using System.Linq.Expressions;

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

        public static implicit operator Expression(TypeReference typeReference) => new TypeReferenceExpression(typeReference);

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
    }
}
