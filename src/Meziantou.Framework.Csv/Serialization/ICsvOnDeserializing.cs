namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Notifies an object before its members are set, when reading CSV data.</summary>
public interface ICsvOnDeserializing
{
    /// <summary>Called before its members are set, when reading CSV data.</summary>
    void OnDeserializing();
}
