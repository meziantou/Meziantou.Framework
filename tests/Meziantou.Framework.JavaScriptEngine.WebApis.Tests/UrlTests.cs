namespace Meziantou.Framework.JavaScriptEngine.WebApis.Tests;

public sealed class UrlTests
{
    [Fact]
    public void Url_Components()
    {
        using var context = new WebApisTestContext();
        Assert.Equal(
            "https://user:pass@example.com:8080/a/b?c=d#e|https://example.com:8080|https:|user|pass|example.com:8080|example.com|8080|/a/b|?c=d|#e",
            context.Run("""
                const u = new URL('HTTPS://user:pass@EXAMPLE.com:8080/a/./x/../b?c=d#e');
                [u.href, u.origin, u.protocol, u.username, u.password, u.host, u.hostname, u.port, u.pathname, u.search, u.hash].join('|')
                """));
        Assert.Equal("https://example.com/", context.Run("new URL('https://example.com:443').href"));
        Assert.Equal("https://xn--bcher-kva.example/", context.Run("new URL('https://Bücher.example').href"));
        Assert.Equal("null", context.Run("new URL('data:text/plain,a').origin"));
    }

    [Fact]
    public void Url_RelativeToBase()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("https://a.com/b/x", context.Run("new URL('../x', 'https://a.com/b/c/d').href"));
        Assert.Equal("https://a.com/b/c/d?q", context.Run("new URL('?q', 'https://a.com/b/c/d').href"));
        Assert.Equal("http://other/", context.Run("new URL('//other', 'http://a.com').href"));
        Assert.Equal("https://a.com/x", context.Run("new URL('/x', new URL('https://a.com/b')).href"));
    }

    [Fact]
    public void Url_Invalid()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("TypeError", context.GetErrorName("new URL('not a url')"));
        Assert.Equal("TypeError", context.GetErrorName("new URL('/x', 'not a base')"));
        Assert.Equal("TypeError", context.GetErrorName("new URL()"));
        Assert.Equal("TypeError", context.GetErrorName("URL('https://a.com')"));
        Assert.Equal("TypeError", context.GetErrorName("new URL('https://a.com').href = 'x'"));
        Assert.Equal("TypeError", context.GetErrorName("Object.getOwnPropertyDescriptor(URL.prototype, 'href').get.call({})"));
        Assert.Equal("false,true,true,false", context.Run("[URL.canParse('x'), URL.canParse('https://a'), URL.canParse('/x', 'https://a'), URL.canParse('/x')].join()"));
        Assert.Equal("null,https://a/x", context.Run("[String(URL.parse('x')), URL.parse('/x', 'https://a').href].join()"));
        Assert.Equal("true", context.Run("URL.parse('https://a') instanceof URL"));
    }

    [Fact]
    public void Url_Setters()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("http://u%20x:p@b.com:81/p%20q?x=1#h%20i", context.Run("""
            const u = new URL('https://a.com');
            u.protocol = 'http';
            u.username = 'u x';
            u.password = 'p';
            u.hostname = 'b.com';
            u.port = '81';
            u.pathname = '/p q';
            u.search = 'x=1';
            u.hash = 'h i';
            u.href
            """));
        Assert.Equal("http://u%20x:p@b.com/p%20q?x=1#h%20i", context.Run("u.port = ''; u.href"));
        Assert.Equal("http://u%20x:p@c.com:90/p%20q?x=1#h%20i", context.Run("u.host = 'c.com:90'; u.href"));
        Assert.Equal("https://z/", context.Run("u.href = 'https://z'; u.href"));
        Assert.Equal("http://a.com/", context.Run("const v = new URL('http://a.com'); v.protocol = 'mailto'; v.href"));
    }

    [Fact]
    public void Url_SearchParamsIsLive()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("1|https://a.com/?b=2&c=3|true", context.Run("""
            const u = new URL('https://a.com/?a=1');
            const first = u.searchParams.get('a');
            u.search = 'b=2';
            u.searchParams.append('c', '3');
            [first, u.href, u.searchParams === u.searchParams].join('|')
            """));
        Assert.Equal("https://a.com/", context.Run("u.searchParams.delete('b'); u.searchParams.delete('c'); u.href"));
        Assert.Equal("x", context.Run("u.href = 'https://a.com/?x=y'; [...u.searchParams.keys()].join()"));
    }

    [Fact]
    public void Url_Json()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("{\"u\":\"https://a.com/\"}", context.Run("JSON.stringify({ u: new URL('https://a.com') })"));
        Assert.Equal("https://a.com/", context.Run("`${new URL('https://a.com')}`"));
    }

    [Fact]
    public void UrlSearchParams_Operations()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("1,3", context.Run("const p = new URLSearchParams('?a=1&b=2&a=3'); p.getAll('a').join()"));
        Assert.Equal("1|null|3|true|false|true", context.Run("[p.get('a'), String(p.get('x')), p.size, p.has('b'), p.has('b', '9'), p.has('a', '3')].join('|')"));
        Assert.Equal("a=1&b=2", context.Run("p.delete('a', '3'); p.toString()"));
        Assert.Equal("a=4&b=2", context.Run("p.set('a', '4'); p.toString()"));
        Assert.Equal("a=4&b=2&c=5", context.Run("p.append('c', 5); p.toString()"));
        Assert.Equal("a=1&a=0&b=2", context.Run("const s = new URLSearchParams('b=2&a=1&a=0'); s.sort(); s.toString()"));
        Assert.Equal("q=a+b%26c%3D%E2%82%AC", context.Run("new URLSearchParams({ q: 'a b&c=€' }).toString()"));
        Assert.Equal("a b", context.Run("new URLSearchParams('q=a+b').get('q')"));
        Assert.Equal("%EF%BF%BD=x", context.Run("new URLSearchParams([['\\uD800', 'x']]).toString()"));
    }

    [Fact]
    public void UrlSearchParams_Initializers()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("a=1&b=2", context.Run("new URLSearchParams([['a', 1], ['b', 2]]).toString()"));
        Assert.Equal("a=1&b=2", context.Run("new URLSearchParams(new Map([['a', 1], ['b', 2]])).toString()"));
        Assert.Equal("a=1", context.Run("new URLSearchParams(Object.defineProperty({ a: 1 }, Symbol('x'), { value: 2, enumerable: false })).toString()"));
        Assert.Equal("TypeError", context.GetErrorName("new URLSearchParams({ a: 1, [Symbol('x')]: 2 })"));
        Assert.Equal("a=1", context.Run("new URLSearchParams(new URLSearchParams('a=1')).toString()"));
        Assert.Equal("", context.Run("new URLSearchParams().toString()"));
        Assert.Equal("undefined=", context.Run("new URLSearchParams('undefined').toString()"));
        Assert.Equal("TypeError", context.GetErrorName("new URLSearchParams([['a']])"));
        Assert.Equal("TypeError", context.GetErrorName("new URLSearchParams([1])"));
        Assert.Equal("TypeError", context.GetErrorName("new URLSearchParams().append('a')"));
    }

    [Fact]
    public void UrlSearchParams_Iteration()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("a=1;b=2", context.Run("const p = new URLSearchParams('a=1&b=2'); [...p].map(([k, v]) => `${k}=${v}`).join(';')"));
        Assert.Equal("a,b|1,2", context.Run("[[...p.keys()].join(), [...p.values()].join()].join('|')"));
        Assert.Equal("[object URLSearchParams Iterator]", context.Run("Object.prototype.toString.call(p.entries())"));
        Assert.Equal("true", context.Run("p[Symbol.iterator] === p.entries"));
        Assert.Equal("1a,2b", context.Run("const items = []; p.forEach((v, k, o) => items.push(v + k + (o === p ? '' : '!'))); items.join()"));

        // The iterator reads the list at each step
        Assert.Equal("a,b,c", context.Run("const keys = []; for (const [k] of p) { keys.push(k); if (k === 'a') p.append('c', '3'); } keys.join()"));
    }
}
