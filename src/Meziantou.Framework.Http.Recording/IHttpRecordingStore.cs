namespace Meziantou.Framework.Http.Recording;

/// <summary>Defines the contract for loading and saving recorded HTTP interactions.</summary>
public interface IHttpRecordingStore
{
    /// <summary>Loads all previously recorded entries. Returns an empty collection if no recording exists.</summary>
    ValueTask<IReadOnlyList<HttpRecordingEntry>> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Saves all recorded entries, replacing any previous data.</summary>
    ValueTask SaveAsync(IReadOnlyList<HttpRecordingEntry> entries, CancellationToken cancellationToken);
}
