namespace Meziantou.Framework.CodeDom
{
    public class CodeCustomAttribute : CodeObject, ICommentable
    {
        private CodeTypeReference _type;

        public CodeCustomAttribute()
            : this(null)
        {
        }

        public CodeCustomAttribute(CodeTypeReference typeReference)
        {
            Arguments = new CodeObjectCollection<CodeCustomAttributeArgument>(this);
            CommentsBefore = new CodeCommentCollection(this);
            CommentsAfter = new CodeCommentCollection(this);
            Type = typeReference;
        }

        public CodeTypeReference Type
        {
            get { return _type; }
            set { _type = SetParent(value); }
        }

        public CodeObjectCollection<CodeCustomAttributeArgument> Arguments { get; }
        public CodeCommentCollection CommentsBefore { get; }
        public CodeCommentCollection CommentsAfter { get; }
    }
}