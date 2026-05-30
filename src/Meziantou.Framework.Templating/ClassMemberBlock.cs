namespace Meziantou.Framework.Templating;

/// <summary>Represents a class member block in a template.</summary>
public class ClassMemberBlock(Template template, string text, int index)
    : TemplateBlock(template, text, index)
{
    /// <summary>Builds the C# code for this class member block.</summary>
    /// <returns>The generated C# class member code.</returns>
    public override string BuildCode()
    {
        return Text;
    }
}
