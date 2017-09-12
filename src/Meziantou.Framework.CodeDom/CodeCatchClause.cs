namespace Meziantou.Framework.CodeDom
{
    public class CodeCatchClause : CodeObject
    {
        private CodeTypeReference _exceptionType;
        private string _exceptionVariableName;
        private CodeStatementCollection _body;

        public string ExceptionVariableName { get; set; }

        public CodeTypeReference ExceptionType
        {
            get => _exceptionType;
            set => _exceptionType = SetParent(value);
        }

        public CodeStatementCollection Body
        {
            get => _body;
            set => _body = SetParent(value);
        }
    }
}
