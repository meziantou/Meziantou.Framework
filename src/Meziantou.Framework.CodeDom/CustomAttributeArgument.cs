#nullable disable
namespace Meziantou.Framework.CodeDom
{
    public class CustomAttributeArgument : CodeObject, ICommentable
    {
        private Expression _value;

        public CustomAttributeArgument()
            : this(propertyName: null, value: null)
        {
        }

        public CustomAttributeArgument(Expression value)
            : this(propertyName: null, value)
        {
        }

        public CustomAttributeArgument(string propertyName, Expression value)
        {
            CommentsBefore = new CommentCollection(this);
            CommentsAfter = new CommentCollection(this);

            PropertyName = propertyName;
            Value = value;
        }

        public CommentCollection CommentsBefore { get; }
        public CommentCollection CommentsAfter { get; }
        public string PropertyName { get; set; }

        public Expression Value
        {
            get => _value;
            set => SetParent(ref _value, value);
        }
    }
}
