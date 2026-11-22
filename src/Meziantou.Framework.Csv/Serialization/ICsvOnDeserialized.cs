namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Notifies an object after its members are set, when reading CSV data.</summary>
public interface ICsvOnDeserialized
{
    /// <summary>Called after its members are set, when reading CSV data.</summary>
    void OnDeserialized();
}
