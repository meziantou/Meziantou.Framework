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
            var name = value.ToString();
            return new CodeMemberReferenceExpression(new CodeTypeReference(type), name);
        }

        public static implicit operator CodeExpression(Type type)
        {
            return new CodeTypeReference(type);
        }

        public static implicit operator CodeExpression(CodeVariableDeclarationStatement variableDeclarationStatement)
        {
            return new CodeVariableReference(variableDeclarationStatement);
        }

        public static implicit operator CodeExpression(CodeMethodArgumentDeclaration argument)
        {
            return new CodeArgumentReferenceExpression(argument);
        }

        public static implicit operator CodeExpression(byte value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(sbyte value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(short value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(ushort value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(int value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(uint value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(long value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(ulong value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(float value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(double value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(decimal value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(string value)
        {
            return new CodeLiteralExpression(value);
        }

        public static implicit operator CodeExpression(bool value)
        {
            return new CodeLiteralExpression(value);
        }
    }
}