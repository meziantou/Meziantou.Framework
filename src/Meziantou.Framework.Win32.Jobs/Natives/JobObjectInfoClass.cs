namespace Meziantou.Framework.Win32.Natives
{
    // https://docs.microsoft.com/en-us/windows/desktop/api/jobapi2/nf-jobapi2-queryinformationjobobject
    internal enum JobObjectInfoClass
    {
        BasicAccountingInformation = 1,
        BasicLimitInformation,
        BasicProcessIdList,
        BasicUIRestrictions,
        SecurityLimitInformation,
        EndOfJobTimeInformation,
        AssociateCompletionPortInformation,
        BasicAndIoAccountingInformation,
        ExtendedLimitInformation,
        JobSetInformation,
        GroupInformation,
        NotificationLimitInformation,
        LimitViolationInformation,
        GroupInformationEx,
        CpuRateControlInformation,
        CompletionFilter,
        CompletionCounter,
        NetRateControlInformation = 32,
        NotificationLimitInformation2,
        LimitViolationInformation2,
        CreateSilo,
        SiloBasicInformation,
    }
}
