namespace Meziantou.Framework.CodeDom
{
    public class CodeCustomAttribute : CodeObject
    {
        private CodeTypeReference _type;

        public CodeCustomAttribute()
            : this(null)
        {
        }

        public CodeCustomAttribute(CodeTypeReference typeReference)
        {
            Arguments = new CodeObjectCollection<CodeCustomAttributeArgument>(this);
            Type = typeReference;
        }

        public CodeTypeReference Type
        {
            get { return _type; }
            set { _type = SetParent(value); }
        }

        public CodeObjectCollection<CodeCustomAttributeArgument> Arguments { get; }
    }
}