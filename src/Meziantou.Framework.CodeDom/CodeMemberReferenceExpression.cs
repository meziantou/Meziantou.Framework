namespace Meziantou.Framework.CodeDom
{
    public class CodeMemberReferenceExpression : CodeExpression
    {
        private CodeMemberDeclaration _memberDeclaration;
        private string _name;
        private CodeExpression _targetObject;

        public CodeMemberReferenceExpression()
        {
        }

        public CodeMemberReferenceExpression(CodeMemberDeclaration memberDeclaration)
        {
            TargetObject = new CodeThisExpression();
            _memberDeclaration = memberDeclaration;
        }

        public CodeMemberReferenceExpression(CodeExpression targetObject, CodeMemberDeclaration memberDeclaration)
        {
            TargetObject = targetObject;
            _memberDeclaration = memberDeclaration;
        }

        public CodeMemberReferenceExpression(CodeExpression targetObject, string memberName)
        {
            TargetObject = targetObject;
            Name = memberName;
        }

        public string Name
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

        public CodeExpression TargetObject
        {
            get => _targetObject;
            set => SetParent(ref _targetObject, value);
        }
    }
}