using System.Reflection;

namespace Meziantou.Framework.Yamlish;

internal sealed record YamlishConstructorInfo(ConstructorInfo Constructor, YamlishConstructorParameterInfo[] Parameters);
