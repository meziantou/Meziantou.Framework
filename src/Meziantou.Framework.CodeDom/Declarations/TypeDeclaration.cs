#nullable disable
namespace Meziantou.Framework.CodeDom
{
    public abstract class TypeDeclaration : CodeObject, ICustomAttributeContainer, ICommentable, IXmlCommentable
    {
        public string Name { get; set; }
        public Modifiers Modifiers { get; set; }
        public CodeObjectCollection<CustomAttribute> CustomAttributes { get; }
        public CommentCollection CommentsBefore { get; }
        public CommentCollection CommentsAfter { get; }
        public XmlCommentCollection XmlComments { get; }

        protected TypeDeclaration()
        {
            CustomAttributes = new CodeObjectCollection<CustomAttribute>(this);
            CommentsBefore = new CommentCollection(this);
            CommentsAfter = new CommentCollection(this);
            XmlComments = new XmlCommentCollection(this);
        }

        public string Namespace => this.GetSelfOrParentOfType<NamespaceDeclaration>()?.Name;

    }
}
