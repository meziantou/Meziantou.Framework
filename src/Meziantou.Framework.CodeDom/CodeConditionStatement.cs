namespace Meziantou.Framework.CodeDom
{
    public class CodeConditionStatement : CodeStatement
    {
        private CodeExpression _condition;
        private CodeStatementCollection _trueStatements;
        private CodeStatementCollection _falseStatements;

        public CodeExpression Condition
        {
            get { return _condition; }
            set { _condition = SetParent(value); }
        }

        public CodeStatementCollection TrueStatements
        {
            get { return _trueStatements; }
            set { _trueStatements = SetParent(value); }
        }

        public CodeStatementCollection FalseStatements
        {
            get { return _falseStatements; }
            set { _falseStatements = SetParent(value); }
        }

        public static CodeConditionStatement CreateIfNotNull(CodeExpression leftExpression)
        {
            CodeConditionStatement condition = new CodeConditionStatement();
            condition.Condition = new CodeBinaryExpression(BinaryOperator.NotEquals, leftExpression, new CodeLiteralExpression(null));
            return condition;
        }
    }
}