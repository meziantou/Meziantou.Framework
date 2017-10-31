using System.Linq;

namespace Meziantou.Framework.CodeDom
{
    internal static class ParametrableTypeExtensions
    {
        public static bool HasConstraints(this CodeTypeParameter parameter)
        {
            return parameter.Constraints.Any();
        }

        public static bool HasConstraints(this IParametrableType parametrable)
        {
            return parametrable.Parameters.Any(HasConstraints);
        }
    }
}
