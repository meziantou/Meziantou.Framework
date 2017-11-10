namespace Meziantou.Framework.CodeDom
{
    public class FieldDeclaration : MemberDeclaration
    {
        private Expression _initExpression;
        private TypeReference _type;

        public FieldDeclaration()
            : this(null, null)
        {
        }

        public FieldDeclaration(string name, TypeReference type)
            : this(name, type, Modifiers.None)
        {
        }

        public FieldDeclaration(string name, TypeReference type, Modifiers modifiers)
            : this(name, type, modifiers, null)
        {
        }

        public FieldDeclaration(string name, TypeReference type, Modifiers modifiers, Expression initExpression)
            : base(name)
        {
            Modifiers = modifiers;
            Type = type;
            InitExpression = initExpression;
        }

        public Expression InitExpression
        {
            get { return _initExpression; }
            set { SetParent(ref _initExpression, value); }
        }

        public TypeReference Type
        {
            get { return _type; }
            set { SetParent(ref _type, value); }
        }

        public Modifiers Modifiers { get; set; }
    }
}