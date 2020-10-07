namespace Meziantou.Framework.DependencyScanning.Internals
{
    internal interface IEnabledScannersArray
    {
        bool IsEmpty { get; }

        void Set(int index);

        bool Get(int index);
    }
}
