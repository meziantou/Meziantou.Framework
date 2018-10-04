using System.Globalization;

namespace Meziantou.Framework
{
    public interface ILocalizationProvider
    {
        string GetString(string name, CultureInfo culture);
    }
}
