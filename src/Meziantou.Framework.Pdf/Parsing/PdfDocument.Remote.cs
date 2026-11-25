using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    // The objects a prefetch of a page reads at most
    private const int MaxPrefetchedObjects = 100_000;

    /// <summary>
    /// Opens a document over HTTP, downloading only the parts of the file it reads (range requests, RFC 9110, 14): the
    /// end of the file and its cross-reference sections when it is opened, then the objects when they are first used. A
    /// server that does not support range requests sends the whole file, which is read from memory.
    /// </summary>
    /// <param name="httpClient">The client that sends the requests. It must stay usable while the document is used.</param>
    /// <param name="uri">The URI of the file.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the downloads of the opening.</param>
    /// <returns>The document.</returns>
    /// <remarks>
    /// <para>
    /// The file is downloaded in blocks of 16 KiB, which are kept: each byte is downloaded once. The first page of a
    /// linearized file is downloaded with the first request after the start of the file.
    /// </para>
    /// <para>
    /// After the document is opened, an object read for the first time is downloaded synchronously, with
    /// <see cref="HttpClient.Send(HttpRequestMessage, CancellationToken)"/>, which some platforms (the browser) do not
    /// support: call <see cref="PrefetchPageAsync(int, CancellationToken)"/> to download what a page needs first. A network
    /// error then throws <see cref="HttpRequestException"/> where the object is used. The file must not change while
    /// the document is used.
    /// </para>
    /// </remarks>
    /// <exception cref="HttpRequestException">The server returned an error, or a range that is not the one requested.</exception>
    /// <exception cref="InvalidPdfContentException">The file is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The file must be repaired, which reads the whole file, and <see cref="PdfReadOptions.AllowFullDownload"/> is <see langword="false"/>.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and the password of the options does not open it.</exception>
    public static async Task<PdfDocument> OpenAsync(HttpClient httpClient, Uri uri, PdfReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(uri);
        options ??= PdfReadOptions.Default;
        var (input, wholeFile) = await HttpRangePdfInput.OpenAsync(httpClient, uri, options.AllowFullDownload, cancellationToken).ConfigureAwait(false);
        if (wholeFile is not null)
            return Open(wholeFile, options);

        var start = new byte[(int)Math.Min(input!.Length, HttpRangePdfInput.BlockSize)];
        start = start[..input.Read(0, start)];
        if (LinearizationReader.TryRead(start, input.Length) is { } linearization)
        {
            await input.PrefetchAsync([(0, (int)Math.Min(linearization.EndOfFirstPage, int.MaxValue))], cancellationToken).ConfigureAwait(false);
        }

        return await input.RunDeferred(() => Create(PdfFileReader.Open(input, options), options), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads what a page of a document opened over HTTP needs and that is not downloaded yet: its objects, the data of
    /// its content streams, of its resources (fonts, images, forms) and of its annotations. The page is then read without
    /// network requests. The method does nothing for a document that was not opened over HTTP. An object that cannot be
    /// read is skipped: reading the page reports the error if the page needs the object.
    /// </summary>
    /// <param name="pageIndex">The zero-based index of the page.</param>
    /// <param name="cancellationToken">The token to cancel the downloads.</param>
    /// <returns>A task that completes when the page is downloaded.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not the index of a page.</exception>
    /// <exception cref="HttpRequestException">The server returned an error.</exception>
    public Task PrefetchPageAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        if (Reader?.Input is not HttpRangePdfInput input)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, Pages.Count);
            return Task.CompletedTask;
        }

        return input.RunDeferred(() => ReadPageObjects(pageIndex), cancellationToken);
    }

    /// <summary>Reads the objects a page uses and the data of their streams, without the other pages they refer to (destinations, parents).</summary>
    private bool ReadPageObjects(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, Pages.Count);
        var page = Pages[pageIndex];
        var visited = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<PdfObject>();
        pending.Push(page.Dictionary);
        if (page.Resources is { } resources)
        {
            pending.Push(resources);
        }

        // What rendering reads outside the page: the layers of the document, and the form of the widgets (its default
        // resources and appearance)
        foreach (var key in new[] { PdfName.Create("OCProperties"), PdfNames.AcroForm })
        {
            if (Catalog[key] is { } value)
            {
                pending.Push(value);
            }
        }

        // An object that cannot be read (damaged, nested too deeply, ...) is skipped: its bytes are downloaded, and reading
        // the page reports the error only if the page needs the object (fuzz regression: an object the text does not use)
        while (pending.Count > 0 && visited.Count < MaxPrefetchedObjects)
        {
            var value = pending.Pop();
            if (value is PdfReference reference)
            {
                try
                {
                    value = reference.Resolve();
                }
                catch (PdfException)
                {
                    continue;
                }
            }

            if (!visited.Add(value))
                continue;

            switch (value)
            {
                case PdfStream stream:
                    try
                    {
                        _ = stream.EncodedData;
                    }
                    catch (PdfException)
                    {
                    }

                    PushEntries(stream.Dictionary);
                    break;
                case PdfDictionary dictionary when ReferenceEquals(dictionary, page.Dictionary) || !(dictionary.IsType(PdfNames.Page) || dictionary.IsType(PdfNames.Pages)):
                    PushEntries(dictionary);
                    break;
                case PdfArray array:
                    foreach (var item in array)
                    {
                        pending.Push(item);
                    }

                    break;
            }
        }

        if (pending.Count > 0)
            throw new PdfResourceLimitException($"The page uses more than {MaxPrefetchedObjects} objects: they are not all downloaded");

        return true;

        void PushEntries(PdfDictionary dictionary)
        {
            // The other pages, which the page tree, the annotations (/P) and the destinations lead to, are not followed:
            // a page or page tree node is not entered, except the page itself. The parents of the form fields are followed
            foreach (var (_, item) in dictionary)
            {
                if (item is not null)
                {
                    pending.Push(item);
                }
            }
        }
    }
}
