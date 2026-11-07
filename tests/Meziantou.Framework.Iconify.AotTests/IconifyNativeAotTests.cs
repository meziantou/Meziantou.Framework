using System.Runtime.CompilerServices;
using System.Text;

namespace Meziantou.Framework.IconifyAotTests;

public sealed class IconifyNativeAotTests
{
    [Fact]
    public void GeneratedIcon()
    {
        var icon = Iconify.Aot.Icons.Used;
        Assert.Equal("used", icon.Name);
        Assert.Equal("""<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><path d="M1 1h22v22H1z" id="used-icon"/></svg>""", icon.Svg);
        Assert.Equal("""<g transform="rotate(90 12 12)"><path d="M1 1h22v22H1z" id="used-icon"/></g>""", Iconify.Aot.Icons.UsedRotated.Body);
    }

    [Fact]
    public void Load()
    {
        var iconSet = Iconify.Load(Path.Combine(AppContext.BaseDirectory, "aot-icons.json"));
        Assert.Equal("aot", iconSet.Prefix);
        Assert.Equal(3, iconSet.Count);
        Assert.Equal(24d, iconSet["used-rotated"].Width);
    }

    [Fact]
    public void UnusedIconsAreTrimmed()
    {
        // Under the JIT, the process is the test host and the check is meaningless
        if (RuntimeFeature.IsDynamicCodeSupported)
            return;

        // Build the marker at run time, so the test itself does not put it in the executable
        var marker = new string("noci-decnerefer-reven".Reverse().ToArray());
        var used = new string("noci-desu".Reverse().ToArray());
        var executable = File.ReadAllBytes(Environment.ProcessPath!);
        // The generated code stores the bodies as UTF-8 data, while the string literals of this file are UTF-16
        Assert.True(Contains(executable, Encoding.UTF8.GetBytes(used)), "The executable must contain the used icon");
        Assert.False(Contains(executable, Encoding.Unicode.GetBytes(marker)), "The executable must not contain the unused icon");
        Assert.False(Contains(executable, Encoding.UTF8.GetBytes(marker)), "The executable must not contain the unused icon");
    }

    private static bool Contains(byte[] data, byte[] value) => data.AsSpan().IndexOf(value) >= 0;
}
