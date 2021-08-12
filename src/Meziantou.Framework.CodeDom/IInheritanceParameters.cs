using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom;

internal interface IInheritanceParameters
{
    TypeReference? BaseType { get; set; }
    IList<TypeReference> Implements { get; }
}
