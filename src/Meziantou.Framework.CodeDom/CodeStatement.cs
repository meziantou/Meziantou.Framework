namespace Meziantou.Framework.CodeDom
{
    public class CodeStatement : CodeObject
    {
        public static implicit operator CodeStatement(CodeExpression expression)
        {
            return new CodeExpressionStatement(expression);
        }
    }
}