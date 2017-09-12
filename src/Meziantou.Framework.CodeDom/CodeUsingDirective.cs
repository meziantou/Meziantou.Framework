namespace Meziantou.Framework.CodeDom
{
    public class CodeUsingDirective : CodeDirective
    {
        public CodeUsingDirective()
            : this(null)
        {
        }

        public CodeUsingDirective(string ns)
        {
            Namespace = ns;
        }

        public string Namespace { get; set; }
    }
}
