namespace Meziantou.DnsProxy;

internal sealed class RewriteRuleOption
{
    public string Domain { get; set; } = "";

    public string Type { get; set; } = "A";

    public string Value { get; set; } = "";
}
