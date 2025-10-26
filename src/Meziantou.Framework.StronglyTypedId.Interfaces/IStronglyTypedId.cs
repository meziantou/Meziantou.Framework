namespace Meziantou.Framework;

public interface IStronglyTypedId
{
    string ValueAsString { get; }
    Type UnderlyingType { get;  }
}

public interface IStronglyTypedId<T> : IStronglyTypedId
{
    T Value { get; }
}
