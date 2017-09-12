namespace Meziantou.Framework.CodeDom
{
    public class CodeSnippetStatement : CodeStatement
    {
        public CodeSnippetStatement()
        {
        }

        public CodeSnippetStatement(string statement)
        {
            Statement = statement;
        }

        public string Statement { get; set; }
    }
}
