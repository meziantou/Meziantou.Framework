namespace Meziantou.Framework;

internal interface IProcessHandleWithEncoding
{
    Encoding OutputEncoding { get; }

    Encoding ErrorEncoding { get; }
}
