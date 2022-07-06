namespace Meziantou.Framework.SimpleQueryLanguage;

public delegate bool UnhandledPropertyDelegate<T>(T obj, string propertyName, KeyValueOperator @operator, string value);