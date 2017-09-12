namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeMemberDeclaration : CodeObject, ICustomAttributeContainer
    {
        public CodeMemberDeclaration()
        {
            CustomAttributes = new CodeObjectCollection<CodeCustomAttribute>(this);
            Implements = new CodeObjectCollection<CodeMemberReferenceExpression>(this);
        }

        public string Name { get; set; }
        public CodeObjectCollection<CodeCustomAttribute> CustomAttributes { get; }
        public CodeObjectCollection<CodeMemberReferenceExpression> Implements { get; }
    }
}