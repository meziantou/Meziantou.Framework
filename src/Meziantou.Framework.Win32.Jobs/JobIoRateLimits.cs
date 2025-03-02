namespace Meziantou.Framework.Win32;
public sealed class JobIoRateLimits
{
    /// <summary>
    /// The policy for control of the I/O rate. This member can be one of the following values.
    /// </summary>
    public JobIoRateFlags ControlFlags { get; set; }

    /// <summary>
    /// <para>The maximum limit for the I/O rate in I/O operations per second (IOPS). Set to 0 if to specify no limit. When you set both <b>MaxIops</b> and <b>MaxBandwith</b>, the operating system enforces the first limit that the I/O rate reaches.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//jobapi2/ns-jobapi2-jobobject_io_rate_control_information#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public long MaxIops { get; set; }

    /// <summary>
    /// <para>The maximum limit for the I/O rate in bytes per second. Set to 0 to specify no limit. When you set both <b>MaxBandwith</b> and <b>MaxIops</b>, the operating system enforces the first limit that the I/O rate reaches.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//jobapi2/ns-jobapi2-jobobject_io_rate_control_information#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public long MaxBandwidth { get; set; }

    /// <summary>
    /// <para>Sets a minimum I/O rate which the operating system reserves for the job. To make no reservation for the job, set this value to 0. The operating system allows the job to perform I/O operations at this rate, if possible. If the sum of the minimum rates for all jobs exceeds the capacity of the operating system, the rate at which the operating system allows each job to perform I/O operations is proportional to the reservation for the job.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//jobapi2/ns-jobapi2-jobobject_io_rate_control_information#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public long ReservationIops { get; set; }

    /// <summary>
    /// <para>The NT device name for the volume to which you want to apply the policy for the I/O rate. For information about NT device names, see <a href="https://docs.microsoft.com/windows-hardware/drivers/kernel/nt-device-names">NT Device Names</a>. If this member is <b>NULL</b>, the policy for the I/O rate applies to all of the volumes for the operating system. For example, if this member is <b>NULL</b> and the <b>MaxIops</b> member is 100, the maximum limit for the I/O rate for each volume is set to 100 IOPS, instead of setting an aggregate limit for the I/O rate across all volumes of 100 IOPS.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//jobapi2/ns-jobapi2-jobobject_io_rate_control_information#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public string? VolumeName { get; set; }

    /// <summary>
    /// <para>The base size of the normalized I/O unit, in bytes.  For example, if the <c>BaseIoSize</c> member is 8,000, every 8,000 bytes counts as one I/O unit. 4,000 bytes is also one I/O unit in this example, while 8,001 bytes is two I/O units. You  can set the value of this base I/O size by using the <c>StorageBaseIOSize</c> value of the <c>HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\QoS</c> registry key. The value of the <c>BaseIoSize</c> member is subject to the following constraints: </para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//jobapi2/ns-jobapi2-jobobject_io_rate_control_information#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public uint BaseIoSize { get; set; }
}
