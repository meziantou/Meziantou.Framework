namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeTypeDeclaration : CodeObject, ICustomAttributeContainer
    {
        public string Name { get; set; }
        public Modifiers Modifiers { get; set; }
        public CodeObjectCollection<CodeCustomAttribute> CustomAttributes { get; }

        public CodeTypeDeclaration()
        {
            CustomAttributes = new CodeObjectCollection<CodeCustomAttribute>(this);
        }

        public string Namespace
        {
            get
            {
                return this.GetSelfOrParentOfType<CodeNamespaceDeclaration>()?.Name;
            }
        }
    }
}