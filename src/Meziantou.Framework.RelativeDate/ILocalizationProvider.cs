using System.Globalization;

namespace Meziantou.Framework.RelativeDate
{
    public interface ILocalizationProvider
    {
        string GetString(string name, CultureInfo culture);
    }
}
