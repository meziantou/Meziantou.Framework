using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Html
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var m0 = new Mode { CaseInsensitive = true };
        var m1 = new Mode { Scope = "meta", Begin = "<![a-z]", End = ">" };
        var m2 = new Mode { Begin = "\\s" };
        var m3 = new Mode { Scope = "keyword", Begin = "#?[a-z_][a-z1-9_-]+", Illegal = "\\n" };
        var m4 = new Mode { Scope = "string", Begin = "\"", End = "\"", Illegal = "\\n" };
        var m5 = new Mode { Begin = "\\\\[\\s\\S]" };
        var m6 = new Mode { Scope = "string", Begin = "'", End = "'", Illegal = "\\n" };
        var m7 = new Mode { Begin = "\\(", End = "\\)" };
        var m8 = new Mode { Begin = "\\[", End = "\\]" };
        var m9 = new Mode { Scope = "meta", Begin = "<![a-z]", End = ">" };
        var m10 = new Mode { Scope = "comment", Begin = "<!--", End = "-->" };
        var m11 = new Mode { Scope = "doctag", Begin = "[ ]*(?=(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):)", End = "(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):", ExcludeBegin = true };
        var m12 = new Mode { Begin = "[ ]+((?:I|a|is|so|us|to|at|if|in|it|on|[A-Za-z]+['](d|ve|re|ll|t|s|n)|[A-Za-z]+[-][a-z]+|[A-Za-z][a-z]{2,})[.]?[:]?([.][ ]|[ ])){3}" };
        var m13 = new Mode { Begin = "<!\\[CDATA\\[", End = "\\]\\]>" };
        var m14 = new Mode { Scope = "symbol", Begin = "&[a-z]+;|&#[0-9]+;|&#x[a-f0-9]+;" };
        var m15 = new Mode { Scope = "meta", End = "\\?>" };
        var m16 = new Mode { Begin = "<\\?xml" };
        var m17 = new Mode { Begin = "<\\?[a-z][a-z0-9]+" };
        var m18 = new Mode { Scope = "tag", Begin = "<style(?=\\s|>)", End = ">", Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["name"] = ["style"] }) };
        var m19 = new Mode { Illegal = "<", EndsWithParent = true };
        var m20 = new Mode { Scope = "attr", Begin = "[\\p{L}0-9._:-]+" };
        var m21 = new Mode { Begin = "=\\s*" };
        var m22 = new Mode { Scope = "string", EndsParent = true };
        var m23 = new Mode { Begin = "\"", End = "\"" };
        var m24 = new Mode { Begin = "'", End = "'" };
        var m25 = new Mode { Begin = "[^\\s\"'=<>`]+" };
        var m26 = new Mode { End = "<\\/style>", ReturnEnd = true, SubLanguage = "css" };
        var m27 = new Mode { Scope = "tag", Begin = "<script(?=\\s|>)", End = ">", Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["name"] = ["script"] }) };
        var m28 = new Mode { End = "<\\/script>", ReturnEnd = true, SubLanguage = "javascript" };
        var m29 = new Mode { Scope = "tag", Begin = "<>|<\\/>" };
        var m30 = new Mode { Scope = "tag", Begin = "<(?=[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*(?:\\/>|>|\\s))", End = "\\/?>" };
        var m31 = new Mode { Scope = "name", Begin = "[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*" };
        var m32 = new Mode { Scope = "tag", Begin = "<\\/(?=[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*>)" };
        var m33 = new Mode { Scope = "name", Begin = "[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*" };
        var m34 = new Mode { Begin = ">", EndsParent = true };

        m0.Contains = [m1, m10, m13, m14, m15, m18, m27, m29, m30, m32];
        m1.Contains = [m2, m4, m6, m7, m8];
        m2.Contains = [m3];
        m4.Contains = [m5];
        m6.Contains = [m5];
        m7.Contains = [m3];
        m8.Contains = [m9];
        m9.Contains = [m2, m7, m4, m6];
        m10.Contains = [m11, m12];
        m15.Variants = [m16, m17];
        m16.Contains = [m4];
        m18.Contains = [m19];
        m18.Starts = m26;
        m19.Contains = [m20, m21];
        m21.Contains = [m22];
        m22.Variants = [m23, m24, m25];
        m23.Contains = [m14];
        m24.Contains = [m14];
        m27.Contains = [m19];
        m27.Starts = m28;
        m30.Contains = [m31];
        m31.Starts = m19;
        m32.Contains = [m33, m34];

        return m0;
    }
}
