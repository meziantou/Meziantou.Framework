namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeMemberDeclaration : CodeObject, ICustomAttributeContainer, ICommentable
    {
        public CodeMemberDeclaration()
            : this(null)
        {
        }

        public CodeMemberDeclaration(string name)
        {
            CustomAttributes = new CodeObjectCollection<CodeCustomAttribute>(this);
            Implements = new CodeObjectCollection<CodeMemberReferenceExpression>(this);
            CommentsBefore = new CodeCommentCollection(this);
            CommentsAfter = new CodeCommentCollection(this);
            Name = name;
        }

        public string Name { get; set; }
        public CodeObjectCollection<CodeCustomAttribute> CustomAttributes { get; }
        public CodeObjectCollection<CodeMemberReferenceExpression> Implements { get; }
        public CodeCommentCollection CommentsBefore { get; }
        public CodeCommentCollection CommentsAfter { get; }
    }
}