namespace Meziantou.Framework.Assertions;

internal readonly struct AssertionMessageBuilder
{
    private readonly StringBuilder _builder;

    public AssertionMessageBuilder(string header)
    {
        _builder = new StringBuilder(header);
    }

    public AssertionMessageBuilder AppendUserMessage(string label, string? message)
    {
        if (!string.IsNullOrEmpty(message))
        {
            Append(label, message);
        }

        return this;
    }

    public AssertionMessageBuilder AppendGroup(params (string Label, string? Value)[] rows)
    {
        var maxLabelLength = 0;
        foreach (var row in rows)
        {
            maxLabelLength = Math.Max(maxLabelLength, row.Label.Length);
        }

        foreach (var row in rows)
        {
            _builder.Append('\n');
            _builder.Append(row.Label);
            _builder.Append(':');
            _builder.Append(' ', maxLabelLength - row.Label.Length + 1);
            _builder.Append(row.Value);
        }

        return this;
    }

    public AssertionMessageBuilder Append(string label, string? value)
    {
        _builder.Append('\n');
        _builder.Append(label);
        _builder.Append(": ");
        _builder.Append(value);

        return this;
    }

    public override string ToString()
    {
        return _builder.ToString();
    }
}
