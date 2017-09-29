namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeConstructorInitializer : CodeObject
    {
        public CodeConstructorInitializer()
        {
            Arguments = new CodeObjectCollection<CodeExpression>(this);
        }

        public CodeObjectCollection<CodeExpression> Arguments { get; }
    }
}