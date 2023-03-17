namespace Meziantou.Framework.HumanReadable;
public class HumanReadableSerializerException : Exception
{
    public HumanReadableSerializerException()
    {
    }

    public HumanReadableSerializerException(string message) : base(message)
    {
    }

    public HumanReadableSerializerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
