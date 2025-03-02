namespace Meziantou.Framework;

public interface IStronglyTypedId
{
    string ValueAsString { get; }
}

public interface IStronglyTypedId<T> : IStronglyTypedId
{
    T Value { get; }
}
