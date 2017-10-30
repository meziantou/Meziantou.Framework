using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class CodeConstructorBaseInitializer : CodeConstructorInitializer
    {
        public CodeConstructorBaseInitializer()
        {
        }

        public CodeConstructorBaseInitializer(params CodeExpression[] codeExpressions) : base(codeExpressions)
        {
        }

        public CodeConstructorBaseInitializer(IEnumerable<CodeExpression> codeExpressions) : base(codeExpressions)
        {
        }
    }
}