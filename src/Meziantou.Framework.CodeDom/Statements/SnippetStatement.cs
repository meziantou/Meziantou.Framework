#nullable disable
namespace Meziantou.Framework.CodeDom
{
    public class SnippetStatement : Statement
    {
        public SnippetStatement()
        {
        }

        public SnippetStatement(string statement)
        {
            Statement = statement;
        }

        public string Statement { get; set; }
    }
}
