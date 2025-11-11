using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

/// <summary>
/// Base class for TagHelpers that inline external files into HTML elements with caching support.
/// </summary>
/// <remarks>
/// This abstract class provides functionality to read files from the web root, cache their contents,
/// and automatically invalidate the cache when files change. It supports reading files as both text
/// and Base64-encoded strings.
/// </remarks>
public abstract class InlineTagHelper : TagHelper
{
    private const string CacheKeyPrefix = "InlineTagHelper-";

    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IMemoryCache _cache;

    /// <summary>Initializes a new instance of the <see cref="InlineTagHelper"/> class.</summary>
    /// <param name="webHostEnvironment">The web host environment for accessing web root files.</param>
    /// <param name="cache">The memory cache for storing file contents.</param>
    protected InlineTagHelper(IWebHostEnvironment webHostEnvironment, IMemoryCache cache)
    {
        _webHostEnvironment = webHostEnvironment;
        _cache = cache;
    }

    private async Task<T?> GetContentAsync<T>(ICacheEntry entry, string path, Func<IFileInfo, Task<T>> getContent)
        where T : class
    {
        var fileProvider = _webHostEnvironment.WebRootFileProvider;
        var changeToken = fileProvider.Watch(path);

        entry.SetPriority(CacheItemPriority.NeverRemove);
        entry.AddExpirationToken(changeToken);

        var file = fileProvider.GetFileInfo(path);
        if (file is null || !file.Exists)
            return default;

        return await getContent(file);
    }

    /// <summary>Gets the file content as a string with caching support.</summary>
    /// <param name="path">The relative path to the file in the web root.</param>
    /// <returns>The file content as a string, or <see langword="null"/> if the path is <see langword="null"/> or the file doesn't exist.</returns>
    protected Task<string?> GetFileContentAsync(string? path)
    {
        if (path is null)
            return Task.FromResult<string?>(null);

        return _cache.GetOrCreateAsync(CacheKeyPrefix + path, entry =>
        {
            return GetContentAsync(entry, path, ReadFileContentAsStringAsync);
        });
    }

    /// <summary>Gets the file content as a Base64-encoded string with caching support.</summary>
    /// <param name="path">The relative path to the file in the web root.</param>
    /// <returns>The file content as a Base64 string, or <see langword="null"/> if the path is <see langword="null"/> or the file doesn't exist.</returns>
    protected Task<string?> GetFileContentBase64Async(string? path)
    {
        if (path is null)
            return Task.FromResult<string?>(null);

        return _cache.GetOrCreateAsync(CacheKeyPrefix + path, entry =>
        {
            return GetContentAsync(entry, path, ReadFileContentAsBase64Async);
        });
    }

    private static async Task<string> ReadFileContentAsStringAsync(IFileInfo file)
    {
        await using var stream = file.CreateReadStream();
        using var textReader = new StreamReader(stream);
        return await textReader.ReadToEndAsync();
    }

    [SuppressMessage("Usage", "MA0032:Use a cancellation token", Justification = "We don't want to cancel this task as it fills the cache for the next one and should be quick")]
    private static async Task<string> ReadFileContentAsBase64Async(IFileInfo file)
    {
        await using var stream = file.CreateReadStream();
        using var writer = new MemoryStream();

        await stream.CopyToAsync(writer);
        writer.Seek(0, SeekOrigin.Begin);
        return Convert.ToBase64String(writer.ToArray());
    }
}
