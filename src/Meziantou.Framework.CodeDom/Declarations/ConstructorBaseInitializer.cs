using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class ConstructorBaseInitializer : ConstructorInitializer
    {
        public ConstructorBaseInitializer()
        {
        }

        public ConstructorBaseInitializer(params Expression[] codeExpressions) : base(codeExpressions)
        {
        }

        public ConstructorBaseInitializer(IEnumerable<Expression> codeExpressions) : base(codeExpressions)
        {
        }
    }
}
