namespace Meziantou.Framework.CodeDom
{
    public static class FormatExtensions
    {
        public static void Format(CodeObject codeObject)
        {
            DefaultFormatterVisitor.Instance.Visit(codeObject);
        }
    }
}
