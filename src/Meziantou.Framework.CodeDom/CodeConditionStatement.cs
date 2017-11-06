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
            set { SetParent(ref _condition, value); }
        }

        public CodeStatementCollection TrueStatements
        {
            get { return _trueStatements; }
            set { SetParent(ref _trueStatements, value); }
        }

        public CodeStatementCollection FalseStatements
        {
            get { return _falseStatements; }
            set { SetParent(ref _falseStatements, value); }
        }

        public static CodeConditionStatement CreateIfNotNull(CodeExpression leftExpression)
        {
            var condition = new CodeConditionStatement();
            condition.Condition = new CodeBinaryExpression(BinaryOperator.NotEquals, leftExpression, new CodeLiteralExpression(null));
            return condition;
        }
    }
}