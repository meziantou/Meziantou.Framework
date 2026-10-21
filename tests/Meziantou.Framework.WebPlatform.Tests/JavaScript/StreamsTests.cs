using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The Streams Standard (https://streams.spec.whatwg.org/), TextEncoderStream, TextDecoderStream, CompressionStream, DecompressionStream and Blob.stream().</summary>
public sealed class StreamsTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    // Runs a script whose promise sets the title of the document
    private string Run(string script)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => $"<script>({script})().then(r => document.title = r, e => document.title = 'error: ' + e);</script>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document.Title;
    }

    [Fact]
    public void ReadableStream_ReadsTheEnqueuedChunks()
    {
        var result = Run("""
            async () => {
              const stream = new ReadableStream({
                start(controller) { controller.enqueue('a'); controller.enqueue('b'); controller.close(); },
              });
              const chunks = [];
              for await (const chunk of stream) chunks.push(chunk);
              return chunks.join() + ' ' + stream.locked + ' ' + Object.prototype.toString.call(stream);
            }
            """);

        Assert.Equal("a,b false [object ReadableStream]", result);
    }

    [Fact]
    public void PipeThrough_TransformsTheChunks()
    {
        var result = Run("""
            async () => {
              const upper = new TransformStream({ transform(chunk, controller) { controller.enqueue(chunk.toUpperCase()); } });
              const chunks = [];
              await ReadableStream.from(['x', 'y']).pipeThrough(upper).pipeTo(new WritableStream({ write(chunk) { chunks.push(chunk); } }));
              return chunks.join();
            }
            """);

        Assert.Equal("X,Y", result);
    }

    [Fact]
    public void WritableStream_AppliesBackpressure()
    {
        var result = Run("""
            async () => {
              const log = [];
              const stream = new WritableStream({ write(chunk) { log.push(chunk); } }, new CountQueuingStrategy({ highWaterMark: 2 }));
              const writer = stream.getWriter();
              log.push(writer.desiredSize);
              writer.write(1);
              writer.write(2);
              log.push(writer.desiredSize);
              await writer.close();
              return log.join();
            }
            """);

        Assert.Equal("2,0,1,2", result);
    }

    [Fact]
    public void ByteStream_FillsTheBuffersOfABYOBReader()
    {
        var result = Run("""
            async () => {
              const stream = new ReadableStream({
                type: 'bytes',
                pull(controller) {
                  const view = controller.byobRequest.view;
                  view[0] = 1; view[1] = 2;
                  controller.byobRequest.respond(2);
                  controller.close();
                },
              });
              const reader = stream.getReader({ mode: 'byob' });
              const buffer = new ArrayBuffer(4);
              const { value } = await reader.read(new Uint8Array(buffer));
              const end = await reader.read(new Uint8Array(4));
              return [...value].join() + ' ' + buffer.detached + ' ' + end.done;
            }
            """);

        Assert.Equal("1,2 true true", result);
    }

    [Fact]
    public void Tee_ReadsTheChunksTwice()
    {
        var result = Run("""
            async () => {
              const [a, b] = ReadableStream.from([1, 2]).tee();
              const read = async (stream) => { const r = []; for await (const c of stream) r.push(c); return r.join(); };
              return (await read(a)) + ' ' + (await read(b));
            }
            """);

        Assert.Equal("1,2 1,2", result);
    }

    [Fact]
    public void EncodingStreams_EncodeAndDecodeSplitCharacters()
    {
        var result = Run("""
            async () => {
              const chunks = [];
              const text = String.fromCodePoint(0x1F600) + ' ' + String.fromCharCode(0xE9);
              await ReadableStream.from([text.slice(0, 1), text.slice(1)])
                .pipeThrough(new TextEncoderStream())
                .pipeThrough(new TransformStream({ transform(chunk, controller) { for (const byte of chunk) controller.enqueue(new Uint8Array([byte])); } }))
                .pipeThrough(new TextDecoderStream())
                .pipeTo(new WritableStream({ write(chunk) { chunks.push(chunk); } }));
              return chunks.join('') === text;
            }
            """);

        Assert.Equal("true", result);
    }

    [Fact]
    public void CompressionStream_RoundTrip()
    {
        var result = Run("""
            async () => {
              const text = 'hello world '.repeat(1000);
              const results = [];
              for (const format of ['brotli', 'deflate', 'deflate-raw', 'gzip']) {
                const compressed = new Blob([text]).stream().pipeThrough(new CompressionStream(format));
                const chunks = [];
                for await (const chunk of compressed.pipeThrough(new DecompressionStream(format)).pipeThrough(new TextDecoderStream())) chunks.push(chunk);
                results.push(chunks.join('') === text);
              }
              return results.join();
            }
            """);

        Assert.Equal("true,true,true,true", result);
    }

    [Fact]
    public void DecompressionStream_InvalidInput_ErrorsTheStream()
    {
        var result = Run("""
            async () => {
              const results = [];
              try { new CompressionStream('zip'); } catch (e) { results.push(e.name); }
              const ds = new DecompressionStream('gzip');
              const writer = ds.writable.getWriter();
              writer.write(new Uint8Array([1, 2, 3])).catch(() => {});
              try { await ds.readable.getReader().read(); } catch (e) { results.push(e.name); }
              try { await writer.close(); } catch (e) { results.push(e.name); }
              return results.join();
            }
            """);

        Assert.Equal("TypeError,TypeError,TypeError", result);
    }

    [Fact]
    public void Blob_StreamAndTextStream()
    {
        var result = Run("""
            async () => {
              const blob = new Blob(['hello ', 'world']);
              const bytes = [];
              for await (const chunk of blob.stream()) bytes.push(...chunk);
              const text = [];
              for await (const chunk of blob.textStream()) text.push(chunk);
              return bytes.length + ' ' + text.join('');
            }
            """);

        Assert.Equal("11 hello world", result);
    }

    [Fact]
    public void PipeTo_IsAbortedBySignal()
    {
        var result = Run("""
            async () => {
              const controller = new AbortController();
              const readable = new ReadableStream({ cancel(reason) { this.reason = reason; } });
              const promise = readable.pipeTo(new WritableStream(), { signal: controller.signal });
              controller.abort('stop');
              try { await promise; return 'resolved'; } catch (e) { return 'rejected ' + e; }
            }
            """);

        Assert.Equal("rejected stop", result);
    }

    [Fact]
    public void StreamsOfAFrame_CanBeReadByTheParentDocument()
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => """
            <iframe srcdoc="<script>window.stream = ReadableStream.from(['frame']);</script>"></iframe>
            <script>
              window.onload = async () => {
                const stream = frames[0].stream;
                const reader = ReadableStream.prototype.getReader.call(stream);
                const { value } = await reader.read();
                document.title = value + ' ' + (reader instanceof frames[0].ReadableStreamDefaultReader);
              };
            </script>
            """);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Empty(_errors);
        Assert.Equal("frame true", document.Title);
    }
}
