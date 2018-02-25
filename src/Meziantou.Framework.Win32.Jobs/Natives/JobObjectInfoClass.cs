namespace Meziantou.Framework.Win32.Natives
{
    internal enum JobObjectInfoClass
    {
        BasicAccountingInformation = 1,
        BasicLimitInformation,
        BasicProcessIdList,
        BasicUIRestrictions,
        SecurityLimitInformation,  // deprecated
        EndOfJobTimeInformation,
        AssociateCompletionPortInformation,
        BasicAndIoAccountingInformation,
        ExtendedLimitInformation,
        JobSetInformation,
        GroupInformation,
    }

}
