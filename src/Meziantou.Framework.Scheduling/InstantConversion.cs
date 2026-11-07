namespace Meziantou.Framework.Scheduling;

/// <summary>The outcome of converting a wall-clock time to an instant.</summary>
internal enum InstantConversion
{
    Success,
    BeforeMinValue,
    AfterMaxValue,
}
