namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Notifies an object before its members are written.</summary>
public interface ICsvOnSerializing
{
    /// <summary>Called before its members are written.</summary>
    void OnSerializing();
}
