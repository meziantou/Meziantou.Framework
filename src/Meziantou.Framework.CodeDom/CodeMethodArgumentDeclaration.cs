namespace Meziantou.Framework.CodeDom
{
    public class CodeMethodArgumentDeclaration : CodeObject, ICustomAttributeContainer, ICommentable
    {
        private CodeTypeReference _type;
        private CodeExpression _defaultValue;

        public CodeMethodArgumentDeclaration()
            : this(null, null)
        {
        }

        public CodeMethodArgumentDeclaration(CodeTypeReference type, string name)
        {
            CustomAttributes = new CodeObjectCollection<CodeCustomAttribute>(this);
            CommentsBefore = new CodeCommentCollection(this);
            CommentsAfter = new CodeCommentCollection(this);

            Type = type;
            Name = name;
        }

        public CodeCommentCollection CommentsBefore { get; }
        public CodeCommentCollection CommentsAfter { get; }
        public CodeObjectCollection<CodeCustomAttribute> CustomAttributes { get; }
        public string Name { get; set; }

        public CodeTypeReference Type
        {
            get => _type;
            set => SetParent(ref _type, value);
        }

        public CodeExpression DefaultValue
        {
            get => _defaultValue;
            set => SetParent(ref _defaultValue, value);
        }

        public Direction Direction { get; set; }
    }
}