namespace Meziantou.Framework.Templating;

public class TemplateException : Exception
{
    public TemplateException()
    {
    }

    public TemplateException(string message)
        : base(message)
    {
    }

    public TemplateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
