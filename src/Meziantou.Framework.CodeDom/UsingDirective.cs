namespace Meziantou.Framework.CodeDom
{
    public class UsingDirective : Directive
    {
        public UsingDirective()
            : this(null)
        {
        }

        public UsingDirective(string ns)
        {
            Namespace = ns;
        }

        public string Namespace { get; set; }
    }
}
