namespace Meziantou.Framework.FastEnumGenerator.FuzzEnums;

public delegate bool TryParseDelegate<T>(string value, bool ignoreCase, out T result);
