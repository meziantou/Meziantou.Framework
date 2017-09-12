namespace Meziantou.Framework.CodeDom
{
    public class CodeSnippetExpression : CodeExpression
    {
        public CodeSnippetExpression()
        {
        }

        public CodeSnippetExpression(string expression)
        {
            Expression = expression;
        }

        public string Expression { get; set; }
    }
}