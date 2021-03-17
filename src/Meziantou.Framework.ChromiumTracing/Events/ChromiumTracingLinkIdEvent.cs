namespace Meziantou.Framework.ChromiumTracing
{
    public sealed class ChromiumTracingLinkIdEvent : ChromiumTracingEvent
    {
        public override string Type => "=";

        public string? Id { get; set; }
    }
}
