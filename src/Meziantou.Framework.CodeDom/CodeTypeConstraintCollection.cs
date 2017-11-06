namespace Meziantou.Framework.CodeDom
{
    public class CodeTypeParameterConstraintCollection : CodeObjectCollection<CodeTypeParameterConstraint>
    {
        public static implicit operator CodeTypeParameterConstraintCollection(CodeTypeParameterConstraint codeConstraint)
        {
            var collection = new CodeTypeParameterConstraintCollection();
            collection.Add(codeConstraint);
            return collection;
        }
    }
}