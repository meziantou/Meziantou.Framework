namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeTypeDeclaration : CodeObject, ICustomAttributeContainer, ICommentable
    {
        public string Name { get; set; }
        public Modifiers Modifiers { get; set; }
        public CodeObjectCollection<CodeCustomAttribute> CustomAttributes { get; }
        public CodeCommentCollection CommentsBefore { get; }
        public CodeCommentCollection CommentsAfter { get; }

        public CodeTypeDeclaration()
        {
            CustomAttributes = new CodeObjectCollection<CodeCustomAttribute>(this);
            CommentsBefore = new CodeCommentCollection(this);
            CommentsAfter = new CodeCommentCollection(this);
        }

        public string Namespace
        {
            get
            {
                return this.GetSelfOrParentOfType<CodeNamespaceDeclaration>()?.Name;
            }
        }
    }
}