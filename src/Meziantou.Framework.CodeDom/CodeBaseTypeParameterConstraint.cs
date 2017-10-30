namespace Meziantou.Framework.CodeDom
{
    public class CodeBaseTypeParameterConstraint : CodeTypeParameterConstraint
    {
        public CodeBaseTypeParameterConstraint()
        {
        }

        public CodeBaseTypeParameterConstraint(CodeTypeReference type)
        {
            Type = type;
        }

        public CodeTypeReference Type { get; set; }
    }
}