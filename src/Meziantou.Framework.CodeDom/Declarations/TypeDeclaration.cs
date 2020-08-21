namespace Meziantou.Framework.CodeDom
{
    public abstract class TypeDeclaration : CodeObject, ICustomAttributeContainer, ICommentable, IXmlCommentable, INullableContext
    {
        public string? Name { get; set; }
        public Modifiers Modifiers { get; set; }
        public CodeObjectCollection<CustomAttribute> CustomAttributes { get; }
        public CommentCollection CommentsBefore { get; }
        public CommentCollection CommentsAfter { get; }
        public XmlCommentCollection XmlComments { get; }
        public NullableContext NullableContext { get; set; }

        protected TypeDeclaration()
        {
            CustomAttributes = new CodeObjectCollection<CustomAttribute>(this);
            CommentsBefore = new CommentCollection(this);
            CommentsAfter = new CommentCollection(this);
            XmlComments = new XmlCommentCollection(this);
        }

        public string? Namespace => this.GetSelfOrParentOfType<NamespaceDeclaration>()?.Name;

    }
}
