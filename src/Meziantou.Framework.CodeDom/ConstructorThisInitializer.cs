using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class ConstructorThisInitializer : ConstructorInitializer
    {
        public ConstructorThisInitializer()
        {
        }

        public ConstructorThisInitializer(params Expression[] codeExpressions) : base(codeExpressions)
        {
        }

        public ConstructorThisInitializer(IEnumerable<Expression> codeExpressions) : base(codeExpressions)
        {
        }
    }
}