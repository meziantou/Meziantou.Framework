namespace Meziantou.Framework.CodeDom
{
    public abstract class MemberDeclaration : CodeObject, ICustomAttributeContainer, ICommentable
    {
        protected MemberDeclaration()
            : this(null)
        {
        }

        protected MemberDeclaration(string name)
        {
            CustomAttributes = new CodeObjectCollection<CustomAttribute>(this);
            Implements = new CodeObjectCollection<MemberReferenceExpression>(this);
            CommentsBefore = new CommentCollection(this);
            CommentsAfter = new CommentCollection(this);
            Name = name;
        }

        public string Name { get; set; }
        public CodeObjectCollection<CustomAttribute> CustomAttributes { get; }
        public CodeObjectCollection<MemberReferenceExpression> Implements { get; }
        public CommentCollection CommentsBefore { get; }
        public CommentCollection CommentsAfter { get; }
    }
}