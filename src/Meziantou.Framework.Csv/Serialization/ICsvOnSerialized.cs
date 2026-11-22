namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Notifies an object after its members are written.</summary>
public interface ICsvOnSerialized
{
    /// <summary>Called after its members are written.</summary>
    void OnSerialized();
}
