using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class CodeConstructorThisInitializer : CodeConstructorInitializer
    {
        public CodeConstructorThisInitializer()
        {
        }

        public CodeConstructorThisInitializer(params CodeExpression[] codeExpressions) : base(codeExpressions)
        {
        }

        public CodeConstructorThisInitializer(IEnumerable<CodeExpression> codeExpressions) : base(codeExpressions)
        {
        }
    }
}