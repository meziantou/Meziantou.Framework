using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.UnicodeBenchmarks;

/// <summary>The algorithms applied to short strings: internationalized domain names and confusable detection.</summary>
[MemoryDiagnoser]
public class IdentifierBenchmarks
{
    private static readonly string[] Hosts =
    [
        "www.example.com", "B\u00FCcher.example", "xn--bcher-kva.example", "\u4F8B\u3048.\u30C6\u30B9\u30C8", "\u0645\u062B\u0627\u0644.\u0625\u062E\u062A\u0628\u0627\u0631",
        "fa\u00DF.de", "\uFF37\uFF37\uFF37\u3002example\u3002com", "\u043F\u0440\u0438\u043C\u0435\u0440.\u0438\u0441\u043F\u044B\u0442\u0430\u043D\u0438\u0435",
    ];

    private static readonly string[] Identifiers =
    [
        "paypal", "p\u0430yp\u0430l", "Administrator", "\u0391dministrator", "user_name_42", "\u0441\u043E\u043C\u0435\u0442\u0445\u0456\u043D\u0433", "r\u0578\u0585t", "Hello World",
    ];

    [Benchmark]
    public int IdnaToAscii()
    {
        var length = 0;
        foreach (var host in Hosts)
        {
            if (UnicodeIdna.TryToAscii(host, out var result))
            {
                length += result.Length;
            }
        }

        return length;
    }

    [Benchmark]
    public int IdnaToUnicode()
    {
        var length = 0;
        foreach (var host in Hosts)
        {
            length += UnicodeIdna.ToUnicode(host, out _).Length;
        }

        return length;
    }

    [Benchmark]
    public int ConfusableSkeleton()
    {
        var length = 0;
        foreach (var identifier in Identifiers)
        {
            length += Unicode.GetConfusableSkeleton(identifier).Length;
        }

        return length;
    }

    [Benchmark]
    public int AreConfusable()
    {
        var count = 0;
        for (var i = 0; i + 1 < Identifiers.Length; i += 2)
        {
            if (Unicode.AreConfusable(Identifiers[i], Identifiers[i + 1]))
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    public int IsMixedScript()
    {
        var count = 0;
        foreach (var identifier in Identifiers)
        {
            if (Unicode.IsMixedScript(identifier))
            {
                count++;
            }
        }

        return count;
    }
}
