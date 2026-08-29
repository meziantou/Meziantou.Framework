namespace Meziantou.Framework.Win32;

/// <summary>Provides extension methods to interpret an <see cref="AmsiResult"/>.</summary>
public static class AmsiResultExtensions
{
    /// <summary>Determines whether the antimalware provider detected the content as malware.</summary>
    /// <param name="result">The result of the scan.</param>
    /// <returns><see langword="true"/> when the content is considered malware; otherwise, <see langword="false"/>.</returns>
    /// <remarks>This does not cover content blocked by administrator policy. Use <see cref="ShouldBlock"/> to cover both cases.</remarks>
    public static bool IsMalware(this AmsiResult result)
    {
        return result >= AmsiResult.Detected;
    }

    /// <summary>Determines whether an administrator policy blocked the content on this machine.</summary>
    /// <param name="result">The result of the scan.</param>
    /// <returns><see langword="true"/> when an administrator policy blocked the content; otherwise, <see langword="false"/>.</returns>
    public static bool IsBlockedByAdmin(this AmsiResult result)
    {
        return result is >= AmsiResult.BlockedByAdminStart and <= AmsiResult.BlockedByAdminEnd;
    }

    /// <summary>Determines whether the content should be blocked, either because it is malware or because an administrator policy blocked it.</summary>
    /// <param name="result">The result of the scan.</param>
    /// <returns><see langword="true"/> when the content should not be used; otherwise, <see langword="false"/>.</returns>
    public static bool ShouldBlock(this AmsiResult result)
    {
        return result.IsMalware() || result.IsBlockedByAdmin();
    }
}
