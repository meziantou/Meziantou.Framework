namespace Meziantou.Framework.Win32.Natives
{
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
