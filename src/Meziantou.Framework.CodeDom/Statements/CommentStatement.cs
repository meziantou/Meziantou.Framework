#nullable disable
namespace Meziantou.Framework.CodeDom
{
    public class CommentStatement : Statement
    {
        public CommentStatement()
        {
        }

        public CommentStatement(string content)
        {
            Content = content;
        }

        public string Content { get; set; }
    }
}
