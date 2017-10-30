using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeConstructorInitializer : CodeObject
    {
        public CodeConstructorInitializer()
        {
            Arguments = new CodeObjectCollection<CodeExpression>(this);
        }

        public CodeConstructorInitializer(params CodeExpression[] codeExpressions)
            : this((IEnumerable<CodeExpression>)codeExpressions)
        {
        }

        public CodeConstructorInitializer(IEnumerable<CodeExpression> codeExpressions)
        {
            Arguments = new CodeObjectCollection<CodeExpression>(this);
            foreach (var codeExpression in codeExpressions)
            {
                Arguments.Add(codeExpression);
            }
        }

        public CodeObjectCollection<CodeExpression> Arguments { get; }
    }
}