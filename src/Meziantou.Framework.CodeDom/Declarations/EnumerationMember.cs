namespace Meziantou.Framework.CodeDom
{
    public class EnumerationMember : MemberDeclaration
    {
        private Expression? _value;

        public EnumerationMember()
        {
        }

        public EnumerationMember(string? name)
        {
            Name = name;
        }

        public EnumerationMember(string? name, Expression value)
        {
            Name = name;
            Value = value;
        }

        public Expression? Value
        {
            get => _value;
            set => SetParent(ref _value, value);
        }
    }
}
