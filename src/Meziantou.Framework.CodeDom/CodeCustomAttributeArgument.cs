namespace Meziantou.Framework.CodeDom
{
    public class CodeCustomAttributeArgument : CodeObject, ICommentable
    {
        private CodeExpression _value;

        public CodeCustomAttributeArgument()
            : this(null, null)
        {
        }

        public CodeCustomAttributeArgument(CodeExpression value)
            : this(null, value)
        {
        }

        public CodeCustomAttributeArgument(string propertyName, CodeExpression value)
        {
            CommentsBefore = new CodeCommentCollection(this);
            CommentsAfter = new CodeCommentCollection(this);

            PropertyName = propertyName;
            Value = value;
        }

        public CodeCommentCollection CommentsBefore { get; }
        public CodeCommentCollection CommentsAfter { get; }
        public string PropertyName { get; set; }

        public CodeExpression Value
        {
            get { return _value; }
            set { _value = SetParent(value); }
        }
    }
}