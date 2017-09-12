namespace Meziantou.Framework.CodeDom
{
    public interface ITypeDeclarationContainer
    {
        CodeObjectCollection<CodeTypeDeclaration> Types { get; }
    }

    public interface INamespaceDeclarationContainer
    {
        CodeObjectCollection<CodeNamespaceDeclaration> Namespaces { get; }
    }

    public interface IUsingDirectiveContainer
    {
        CodeObjectCollection<CodeUsingDirective> Usings { get; }
    }

    public class CodeCompilationUnit : CodeObject, ITypeDeclarationContainer, INamespaceDeclarationContainer, IUsingDirectiveContainer
    {
        private CodeObjectCollection<CodeTypeDeclaration> _types;
        private CodeObjectCollection<CodeNamespaceDeclaration> _namespaces;
        private CodeObjectCollection<CodeUsingDirective> _usings;

        public CodeCompilationUnit()
        {
        }

        public CodeCompilationUnit(CodeTypeDeclaration typeDeclaration)
        {
            if (typeDeclaration != null)
            {
                Types.Add(typeDeclaration);
            }
        }

        public CodeCompilationUnit(CodeNamespaceDeclaration namespaceDeclaration)
        {
            if (namespaceDeclaration != null)
            {
                Namespaces.Add(namespaceDeclaration);
            }
        }

        public CodeObjectCollection<CodeTypeDeclaration> Types
        {
            get
            {
                if (_types == null)
                {
                    _types = new CodeObjectCollection<CodeTypeDeclaration>(this);
                }
                return _types;
            }
        }

        public CodeObjectCollection<CodeNamespaceDeclaration> Namespaces
        {
            get
            {
                if (_namespaces == null)
                {
                    _namespaces = new CodeObjectCollection<CodeNamespaceDeclaration>(this);
                }
                return _namespaces;
            }
        }

        public CodeObjectCollection<CodeUsingDirective> Usings
        {
            get
            {
                if (_usings == null)
                {
                    _usings = new CodeObjectCollection<CodeUsingDirective>(this);
                }
                return _usings;
            }
        }
    }
}