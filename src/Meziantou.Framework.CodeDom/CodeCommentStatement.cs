namespace Meziantou.Framework.CodeDom
{
    public class CodeCommentStatement : CodeStatement
    {
        public CodeCommentStatement()
        {
        }

        public CodeCommentStatement(string content)
        {
            Content = content;
        }

        public string Content { get; set; }
    }
}