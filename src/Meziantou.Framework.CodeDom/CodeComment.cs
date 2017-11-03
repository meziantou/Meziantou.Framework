namespace Meziantou.Framework.CodeDom
{
    public class CodeComment : CodeObject
    {
        public CodeComment()
        {
        }

        public CodeComment(string text, CodeCommentType type)
        {
            Text = text;
            Type = type;
        }

        public string Text { get; set; }
        public CodeCommentType Type { get; set; }
    }
}
