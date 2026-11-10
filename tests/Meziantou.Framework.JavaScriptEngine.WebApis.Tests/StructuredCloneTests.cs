namespace Meziantou.Framework.JavaScriptEngine.WebApis.Tests;

public sealed class StructuredCloneTests
{
    [Fact]
    public void StructuredClone_Values()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("true", context.Run("""
            const date = new Date(0);
            const source = {
              number: 1, string: 's', bigint: 10n, nested: { array: [1, , 3] },
              date, regexp: /a+/gi, map: new Map([[1, { a: 1 }]]), set: new Set(['x']),
              bytes: new Uint8Array([1, 2, 3]), boxed: new String('b'), negativeZero: -0,
            };
            source.self = source;
            const clone = structuredClone(source);
            clone !== source && clone.self === clone && clone.number === 1 && clone.string === 's' && clone.bigint === 10n
              && clone.nested.array.length === 3 && !(1 in clone.nested.array) && clone.date !== date && clone.date.getTime() === 0
              && clone.regexp.source === 'a+' && clone.regexp.flags === 'gi' && clone.map.get(1).a === 1 && clone.set.has('x')
              && clone.bytes.join() === '1,2,3' && clone.bytes.buffer !== source.bytes.buffer && clone.boxed instanceof String
              && Object.is(clone.negativeZero, -0)
            """));
        Assert.Equal("undefined,null,abc", context.Run("[structuredClone(undefined), structuredClone(null), structuredClone('abc')].map(String).join()"));
    }

    [Fact]
    public void StructuredClone_Errors()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("RangeError|boom|cause|true", context.Run("""
            const error = structuredClone(new RangeError('boom', { cause: 'cause' }));
            [error.name, error.message, error.cause, error instanceof RangeError].join('|')
            """));
        Assert.Equal("NotFoundError|m|true", context.Run("""
            const exception = structuredClone(new DOMException('m', 'NotFoundError'));
            [exception.name, exception.message, exception instanceof DOMException].join('|')
            """));
    }

    [Fact]
    public void StructuredClone_PrototypeIsNotKept()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("false,1", context.Run("class A { constructor() { this.a = 1; } } const c = structuredClone(new A()); [c instanceof A, c.a].join()"));
    }

    [Fact]
    public void StructuredClone_NotCloneable()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("DataCloneError", context.GetErrorName("structuredClone(() => {})"));
        Assert.Equal("DataCloneError", context.GetErrorName("structuredClone({ f() {} })"));
        Assert.Equal("DataCloneError", context.GetErrorName("structuredClone(Symbol())"));
        Assert.Equal("DataCloneError", context.GetErrorName("structuredClone(new WeakMap())"));
        Assert.Equal("25,true", context.Run("try { structuredClone(() => {}); } catch (e) { [e.code, e instanceof DOMException].join() }"));
        Assert.Equal("TypeError", context.GetErrorName("structuredClone()"));
    }

    [Fact]
    public void StructuredClone_Transfer()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("0,1,2,3,true", context.Run("""
            const bytes = new Uint8Array([1, 2, 3]);
            const clone = structuredClone(bytes, { transfer: [bytes.buffer] });
            [bytes.buffer.byteLength, clone.join(), bytes.buffer.detached].join()
            """));
        Assert.Equal("DataCloneError", context.GetErrorName("const b = new ArrayBuffer(1); structuredClone(b, { transfer: [b, b] })"));
        Assert.Equal("DataCloneError", context.GetErrorName("structuredClone(1, { transfer: [{}] })"));
        Assert.Equal("TypeError", context.GetErrorName("structuredClone(1, { transfer: [1] })"));
        Assert.Equal("TypeError", context.GetErrorName("structuredClone(1, { transfer: 1 })"));
    }

    [Fact]
    public void StructuredClone_SharedArrayBuffer()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("false,7", context.Run("""
            const shared = new Int32Array(new SharedArrayBuffer(4));
            const clone = structuredClone(shared);
            shared[0] = 7;
            [clone.buffer === shared.buffer, clone[0]].join()
            """));
    }
}
