namespace Meziantou.DnsProxy;

internal sealed class CustomDnsRecordOption
{
    public string Domain { get; set; } = "";

    public string Type { get; set; } = "A";

    public string Value { get; set; } = "";

    public List<string> Values { get; set; } = [];
}
