using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

namespace Meziantou.AspNetCore.Mvc.TagHelpers
{
    public abstract class InlineTagHelper : TagHelper
    {
        private const string CacheKeyPrefix = "InlineTagHelper-";

        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IMemoryCache _cache;

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
            if (file == null || !file.Exists)
                return default;

            return await getContent(file);
        }

        protected Task<string?> GetFileContentAsync(string? path)
        {
            if (path == null)
                return Task.FromResult<string?>(null);

            return _cache.GetOrCreateAsync(CacheKeyPrefix + path, entry =>
            {
                return GetContentAsync(entry, path, ReadFileContentAsStringAsync);
            });
        }

        protected Task<string?> GetFileContentBase64Async(string? path)
        {
            if (path == null)
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
}
