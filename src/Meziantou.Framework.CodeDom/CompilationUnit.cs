namespace Meziantou.Framework.CodeDom
{
    public class CompilationUnit : CodeObject, ITypeDeclarationContainer, INamespaceDeclarationContainer, IUsingDirectiveContainer
    {
        private CodeObjectCollection<TypeDeclaration>? _types;
        private CodeObjectCollection<NamespaceDeclaration>? _namespaces;
        private CodeObjectCollection<UsingDirective>? _usings;

        public CompilationUnit()
        {
        }

        public CompilationUnit(TypeDeclaration? typeDeclaration)
        {
            if (typeDeclaration != null)
            {
                Types.Add(typeDeclaration);
            }
        }

        public CompilationUnit(NamespaceDeclaration? namespaceDeclaration)
        {
            if (namespaceDeclaration != null)
            {
                Namespaces.Add(namespaceDeclaration);
            }
        }

        public CodeObjectCollection<TypeDeclaration> Types
        {
            get
            {
                if (_types == null)
                {
                    _types = new CodeObjectCollection<TypeDeclaration>(this);
                }

                return _types;
            }
        }

        public CodeObjectCollection<NamespaceDeclaration> Namespaces
        {
            get
            {
                if (_namespaces == null)
                {
                    _namespaces = new CodeObjectCollection<NamespaceDeclaration>(this);
                }

                return _namespaces;
            }
        }

        public CodeObjectCollection<UsingDirective> Usings
        {
            get
            {
                if (_usings == null)
                {
                    _usings = new CodeObjectCollection<UsingDirective>(this);
                }
                return _usings;
            }
        }
    }
}
