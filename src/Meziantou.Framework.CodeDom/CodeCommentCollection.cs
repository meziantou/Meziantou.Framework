namespace Meziantou.Framework.CodeDom
{
    public class CodeCommentCollection : CodeObjectCollection<CodeComment>
    {
        private readonly CodeCommentType _defaultCommentType;

        public CodeCommentCollection(CodeObject parent) : base(parent)
        {
        }

        public CodeCommentCollection(CodeObject parent, CodeCommentType defaultCommentType) : base(parent)
        {
            _defaultCommentType = defaultCommentType;
        }

        public void Add(string text)
        {
            Add(new CodeComment(text, _defaultCommentType));
        }

        public void Add(string text, CodeCommentType type)
        {
            Add(new CodeComment(text, type));
        }
    }
}
