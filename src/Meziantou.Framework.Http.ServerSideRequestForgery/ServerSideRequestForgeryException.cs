namespace Meziantou.Framework.Http.ServerSideRequestForgery;

public sealed class ServerSideRequestForgeryException : Exception
{
    public ServerSideRequestForgeryException()
    {
    }

    public ServerSideRequestForgeryException(string message)
        : base(message)
    {
    }

    public ServerSideRequestForgeryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
