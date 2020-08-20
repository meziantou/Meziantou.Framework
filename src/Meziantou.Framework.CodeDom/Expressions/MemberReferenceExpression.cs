namespace Meziantou.Framework.CodeDom
{
    public class MemberReferenceExpression : Expression
    {
        private MemberDeclaration? _memberDeclaration;
        private string? _name;
        private Expression? _targetObject;

        public MemberReferenceExpression()
        {
        }

        public MemberReferenceExpression(MemberDeclaration? memberDeclaration)
        {
            TargetObject = new ThisExpression();
            _memberDeclaration = memberDeclaration;
        }

        public MemberReferenceExpression(Expression? targetObject, MemberDeclaration? memberDeclaration)
        {
            TargetObject = targetObject;
            _memberDeclaration = memberDeclaration;
        }

        public MemberReferenceExpression(Expression? targetObject, string? memberName)
        {
            TargetObject = targetObject;
            Name = memberName;
        }

        public MemberReferenceExpression(TypeReference? type, string? memberName)
        {
            TargetObject = type != null ? new TypeReferenceExpression(type) : null;
            Name = memberName;
        }

        public string? Name
        {
            get
            {
                if (_memberDeclaration != null)
                    return _memberDeclaration.Name;

                return _name;
            }
            set
            {
                _name = value;
                _memberDeclaration = null;
            }
        }

        public Expression? TargetObject
        {
            get => _targetObject;
            set => SetParent(ref _targetObject, value);
        }
    }
}
