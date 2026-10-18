using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>The conversions of the buffer source types (https://webidl.spec.whatwg.org/#idl-buffer-source-types) by the bindings.</summary>
public sealed class BufferSourceTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    /// <summary>Runs a script and returns the value it assigns to <c>document.title</c>.</summary>
    private string Run(string script)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        // The document is cross-origin isolated, so that SharedArrayBuffer is exposed
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<script>" + script + "</script>")
        {
            Headers = [new("Content-Type", "text/html"), new("Cross-Origin-Opener-Policy", "same-origin"), new("Cross-Origin-Embedder-Policy", "require-corp")],
        });
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document.Title;
    }

    [Fact]
    public void TextEncoder_Encode_ReturnsAUint8Array()
    {
        var result = Run("""
            const bytes = new TextEncoder().encode('aé');
            document.title = [bytes instanceof Uint8Array, bytes.length, bytes.join(' '), bytes.buffer.byteLength].join();
            """);
        Assert.Equal("true,3,97 195 169,3", result);
    }

    [Fact]
    public void TextDecoder_Decode_AcceptsTheBufferSourceTypes()
    {
        var result = Run("""
            const decoder = new TextDecoder();
            const bytes = new Uint8Array([0x78, 0x61, 0x62, 0x63, 0x79]);
            document.title = [
              decoder.decode(bytes),
              decoder.decode(bytes.buffer),
              decoder.decode(bytes.subarray(1, 4)),
              decoder.decode(new DataView(bytes.buffer, 2, 2)),
              decoder.decode(new Uint16Array(bytes.buffer, 0, 2)),
              decoder.decode(new SharedArrayBuffer(0)),
              decoder.decode(),
            ].join('|');
            """);
        Assert.Equal("xabcy|xabcy|abc|bc|xabc||", result);
    }

    [Fact]
    public void TextDecoder_Decode_ThrowsATypeErrorForOtherValues()
    {
        var result = Run("""
            const results = [];
            for (const value of [[1, 2], 'abc', {}, 42]) {
              try { new TextDecoder().decode(value); results.push('no error'); } catch (e) { results.push(e.name); }
            }
            document.title = results.join();
            """);
        Assert.Equal("TypeError,TypeError,TypeError,TypeError", result);
    }

    [Fact]
    public void TextEncoder_EncodeInto_WritesIntoTheTypedArray()
    {
        var result = Run("""
            const buffer = new ArrayBuffer(6);
            const destination = new Uint8Array(buffer, 2, 3);
            const { read, written } = new TextEncoder().encodeInto('abé', destination);
            document.title = [read, written, new Uint8Array(buffer).join(' ')].join();
            """);
        Assert.Equal("2,2,0 0 97 98 0 0", result);
    }

    [Fact]
    public void TextEncoder_EncodeInto_AllowsSharedArrayBuffers()
    {
        var result = Run("""
            const destination = new Uint8Array(new SharedArrayBuffer(2));
            const { written } = new TextEncoder().encodeInto('ab', destination);
            let error = 'no error';
            try { new TextEncoder().encodeInto('a', new Int8Array(2)); } catch (e) { error = e.name; }
            document.title = [written, destination.join(' '), error].join();
            """);
        Assert.Equal("2,97 98,TypeError", result);
    }

    [Fact]
    public void BufferSource_DoesNotAcceptSharedArrayBuffersWithoutAllowShared()
    {
        var result = Run("""
            let error = 'no error';
            try { new Blob([new Uint8Array(new SharedArrayBuffer(1))]); } catch (e) { error = e.name; }
            document.title = error;
            """);
        Assert.Equal("TypeError", result);
    }

    [Fact]
    public void Blob_AcceptsBufferSourceParts()
    {
        var result = Run("""
            const bytes = new TextEncoder().encode('hello');
            const blob = new Blob([bytes, bytes.buffer.slice(0, 1), new DataView(bytes.buffer, 4), 'x']);
            blob.text().then(text => document.title = blob.size + ' ' + text);
            """);
        Assert.Equal("8 hellohox", result);
    }

    [Fact]
    public void DomMatrix_FloatArrays()
    {
        var result = Run("""
            const matrix = DOMMatrix.fromFloat32Array(new Float32Array([1, 2, 3, 4, 5, 6]));
            const array = matrix.toFloat64Array();
            document.title = [array instanceof Float64Array, array.length, array[0], array[4], array[12], array[13]].join();
            """);
        Assert.Equal("true,16,1,3,5,6", result);
    }
}
