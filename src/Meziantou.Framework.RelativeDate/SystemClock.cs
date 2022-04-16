namespace Meziantou.Framework;

internal sealed class SystemClock : IClock
{
    [SuppressMessage("ApiDesign", "RS0030:Do not used banned APIs", Justification = "By design")]
    public DateTime Now => DateTime.Now;

    public DateTime UtcNow => DateTime.UtcNow;
}
