namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The decoder side of a structure parser: receives payload callbacks and builds the image.</summary>
internal interface IImageDecodeObserver : IDisposable
{
    /// <summary>Gets the decoded image once the walk is complete, transferring its ownership.</summary>
    Image GetResult();
}
