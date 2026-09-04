using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

/// <summary>Base class for TagHelpers that inline external files into HTML elements with caching support.</summary>
/// <remarks>
/// This abstract class provides functionality to read files from the web root, cache their contents,
/// and automatically invalidate the cache when files change. It supports reading files as both text
/// and Base64-encoded strings.
/// </remarks>
public abstract partial class InlineTagHelper : TagHelper
{
    // The text and Base64 representations of a file must not share a cache entry, otherwise a file inlined
    // both ways (<inline-img> and <inline-style> for instance) serves whichever encoding was computed first.
    private const string TextCacheKeyPrefix = "InlineTagHelper-text-";
    private const string Base64CacheKeyPrefix = "InlineTagHelper-base64-";

    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IMemoryCache _cache;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="InlineTagHelper"/> class.</summary>
    /// <param name="webHostEnvironment">The web host environment for accessing web root files.</param>
    /// <param name="cache">The memory cache for storing file contents.</param>
    /// <param name="logger">The logger used to report missing files.</param>
    protected InlineTagHelper(IWebHostEnvironment webHostEnvironment, IMemoryCache cache, ILogger logger)
    {
        _webHostEnvironment = webHostEnvironment;
        _cache = cache;
        _logger = logger;
    }

    private async Task<string?> GetContentAsync(ICacheEntry entry, string path, Func<IFileInfo, Task<string>> getContent)
    {
        var fileProvider = _webHostEnvironment.WebRootFileProvider;

        // Watch the path even when the file is missing, so the entry is invalidated if the file is created later
        entry.AddExpirationToken(fileProvider.Watch(path));

        var file = fileProvider.GetFileInfo(path);
        if (!file.Exists)
        {
            LogFileNotFound(path);

            // A size must be set on every entry when the application configures MemoryCacheOptions.SizeLimit
            entry.SetSize(1);
            return null;
        }

        var content = await getContent(file);
        entry.SetSize(content.Length);
        return content;
    }

    /// <summary>Gets the file content as a string with caching support.</summary>
    /// <param name="path">The relative path to the file in the web root.</param>
    /// <returns>The file content as a string, or <see langword="null"/> if the path is <see langword="null"/> or the file doesn't exist.</returns>
    protected Task<string?> GetFileContentAsync(string? path)
    {
        if (path is null)
            return Task.FromResult<string?>(null);

        return _cache.GetOrCreateAsync(TextCacheKeyPrefix + path, entry =>
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

        return _cache.GetOrCreateAsync(Base64CacheKeyPrefix + path, entry =>
        {
            return GetContentAsync(entry, path, ReadFileContentAsBase64Async);
        });
    }

    // The content of <script> and <style> is raw text: the element ends at the first matching close sequence,
    // whatever the JavaScript or CSS syntax around it. "<\/tag" is equivalent inside strings and regexes,
    // and "</tag" cannot legally appear anywhere else.
    internal static string EscapeClosingTag(string content, string tagName)
    {
        return content.Replace("</" + tagName, "<\\/" + tagName, StringComparison.OrdinalIgnoreCase);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot inline '{Path}': the file does not exist in the web root")]
    private partial void LogFileNotFound(string path);
}
