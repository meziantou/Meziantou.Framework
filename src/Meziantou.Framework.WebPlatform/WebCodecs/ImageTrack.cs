namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>The tracks of the image of an <see cref="ImageDecoder"/> (https://w3c.github.io/webcodecs/#imagetracklist), and the selected one.</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#imagetracklist"/>
[IdlInterface("ImageTrackList", "https://w3c.github.io/webcodecs/#imagetracklist")]
public sealed class ImageTrackList
{
    private readonly TaskCompletionSource _ready = new();
    private List<ImageTrack> _tracks = [];

    internal ImageTrackList()
    {
        Scripting.PromiseSlots.MarkAsHandled(_ready.Task);
    }

    /// <summary>Gets a task that completes when the tracks are known, or fails when the decoder is closed before (https://w3c.github.io/webcodecs/#dom-imagetracklist-ready).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagetracklist-ready"/>
    [IdlMember("ready", "https://w3c.github.io/webcodecs/#dom-imagetracklist-ready")]
    public Task Ready => _ready.Task;

    /// <summary>Gets the number of tracks.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagetracklist-length"/>
    [IdlMember("length", "https://w3c.github.io/webcodecs/#dom-imagetracklist-length")]
    public uint Length => (uint)_tracks.Count;

    /// <summary>Gets the index of the selected track, or -1 when no track is selected.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagetracklist-selectedindex"/>
    [IdlMember("selectedIndex", "https://w3c.github.io/webcodecs/#dom-imagetracklist-selectedindex")]
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>Gets the selected track, or null when no track is selected.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagetracklist-selectedtrack"/>
    [IdlMember("selectedTrack", "https://w3c.github.io/webcodecs/#dom-imagetracklist-selectedtrack")]
    public ImageTrack? SelectedTrack => SelectedIndex >= 0 && SelectedIndex < _tracks.Count ? _tracks[SelectedIndex] : null;

    /// <summary>Gets the track at an index, or null when the index is out of range.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagetracklist-__getter__"/>
    [IdlMember("getter", "https://w3c.github.io/webcodecs/#dom-imagetracklist-__getter__")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public ImageTrack? this[uint index] => index < (uint)_tracks.Count ? _tracks[(int)index] : null;

    /// <summary>Whether the tracks have been added to the list (the list is empty before and after the decoder is closed).</summary>
    internal bool HasTracks => _tracks.Count > 0;

    /// <summary>Sets the tracks found in the image and the selected one, then resolves the ready promise (the last steps of https://w3c.github.io/webcodecs/#imagedecoder-establish-tracks).</summary>
    internal void SetTracks(List<ImageTrack> tracks, int selectedIndex)
    {
        _tracks = tracks;
        SelectedIndex = selectedIndex;
        _ready.TrySetResult();
    }

    /// <summary>Rejects the ready promise (https://w3c.github.io/webcodecs/#imagedecoder-close-imagedecoder, when the list is empty).</summary>
    internal void Reject(Exception exception) => _ready.TrySetException(exception);

    /// <summary>Removes the tracks (https://w3c.github.io/webcodecs/#imagedecoder-close-imagedecoder).</summary>
    internal void Clear()
    {
        _tracks = [];
        SelectedIndex = -1;
    }

    /// <summary>Selects a track, or none (https://w3c.github.io/webcodecs/#dom-imagetrack-selected).</summary>
    internal void Select(ImageTrack track, bool selected)
    {
        if (SelectedIndex >= 0 && SelectedIndex < _tracks.Count)
        {
            _tracks[SelectedIndex].SelectedValue = false;
        }

        track.SelectedValue = selected;
        SelectedIndex = selected ? _tracks.IndexOf(track) : -1;
    }
}

/// <summary>A track of the image of an <see cref="ImageDecoder"/> (https://w3c.github.io/webcodecs/#imagetrack): a still image or an animation.</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#imagetrack"/>
[IdlInterface("ImageTrack", "https://w3c.github.io/webcodecs/#imagetrack")]
public sealed class ImageTrack
{
    private readonly ImageDecoder _decoder;
    private readonly ImageTrackList _trackList;

    internal ImageTrack(ImageDecoder decoder, ImageTrackList trackList, bool animated, uint frameCount, float repetitionCount)
    {
        _decoder = decoder;
        _trackList = trackList;
        Animated = animated;
        FrameCount = frameCount;
        RepetitionCount = repetitionCount;
    }

    /// <summary>Gets whether the track is an animation of several frames.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagetrack-animated"/>
    [IdlMember("animated", "https://w3c.github.io/webcodecs/#dom-imagetrack-animated")]
    public bool Animated { get; }

    /// <summary>Gets the number of frames of the track found so far (it increases while the data of a stream arrives).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagetrack-framecount"/>
    [IdlMember("frameCount", "https://w3c.github.io/webcodecs/#dom-imagetrack-framecount")]
    public uint FrameCount { get; internal set; }

    /// <summary>Gets the number of times the animation is repeated after it is played once (<see cref="float.PositiveInfinity"/>: forever).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagetrack-repetitioncount"/>
    [IdlMember("repetitionCount", "https://w3c.github.io/webcodecs/#dom-imagetrack-repetitioncount")]
    public float RepetitionCount { get; }

    /// <summary>Gets or sets whether the track is the one that is decoded (https://w3c.github.io/webcodecs/#dom-imagetrack-selected).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagetrack-selected"/>
    [IdlMember("selected", "https://w3c.github.io/webcodecs/#dom-imagetrack-selected")]
    public bool Selected
    {
        get => SelectedValue;
        set
        {
            // 1-3. Nothing changes when the decoder is closed or the value is the same
            if (_decoder.IsClosed || value == SelectedValue)
                return;

            // 4-9. The selected index of the list
            _trackList.Select(this, value);

            // 10-12. The pending decodes are aborted, and the decoder decodes the new track
            _decoder.OnTrackSelected(_trackList.SelectedIndex);
        }
    }

    /// <summary>The [[selected]] internal slot.</summary>
    internal bool SelectedValue { get; set; }
}
