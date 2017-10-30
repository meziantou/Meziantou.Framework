namespace Meziantou.Framework.CodeDom
{
    public class CodeTypeParameter : CodeObject
    {
        public CodeTypeParameter()
        {
            Constraints = new CodeTypeParameterConstraintCollection();
        }

        public CodeTypeParameter(string name)
            : this()
        {
            Name = name;
        }

        public string Name { get; set; }
        public CodeTypeParameterConstraintCollection Constraints { get; }
    }
}