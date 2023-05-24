namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class DBNullConverter : HumanReadableConverter<DBNull>
{
    public override bool HandleNull => true;

    protected override void WriteValue(HumanReadableTextWriter writer, DBNull? value, HumanReadableSerializerOptions options)
    {
        writer.WriteNullValue();
    }
}

