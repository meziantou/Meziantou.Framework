using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.SimpleQueryLanguage;

public delegate bool ScalarParser<T>(string value, [MaybeNullWhen(false)] out T result);
