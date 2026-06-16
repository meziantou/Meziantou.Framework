using System.Reflection;

namespace Meziantou.Framework.Yamlish.Internals;

internal sealed record YamlishConstructorInfo(ConstructorInfo Constructor, YamlishConstructorParameterInfo[] Parameters);
