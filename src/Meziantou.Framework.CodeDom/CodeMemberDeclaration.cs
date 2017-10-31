namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeMemberDeclaration : CodeObject, ICustomAttributeContainer
    {
        public CodeMemberDeclaration()
            : this(null)
        {
        }

        public CodeMemberDeclaration(string name)
        {
            CustomAttributes = new CodeObjectCollection<CodeCustomAttribute>(this);
            Implements = new CodeObjectCollection<CodeMemberReferenceExpression>(this);
            Name = name;
        }

        public string Name { get; set; }
        public CodeObjectCollection<CodeCustomAttribute> CustomAttributes { get; }
        public CodeObjectCollection<CodeMemberReferenceExpression> Implements { get; }
    }
}