using System;

namespace Meziantou.Framework.CodeDom
{
    public class CodeMethodDeclaration : CodeMemberDeclaration
    {
        private CodeTypeReference _returnType;

        public CodeTypeReference ReturnType
        {
            get { return _returnType; }
            set { _returnType = SetParent(value); }
        }

        public CodeObjectCollection<CodeMethodArgumentDeclaration> Arguments { get; }
        public CodeStatementCollection Statements { get; set; }
        public Modifiers Modifiers { get; set; }

        public CodeMethodDeclaration()
            : this(null)
        {
        }

        public CodeMethodDeclaration(string name)
        {
            Arguments = new CodeObjectCollection<CodeMethodArgumentDeclaration>(this);
            Name = name;
        }
    }
}