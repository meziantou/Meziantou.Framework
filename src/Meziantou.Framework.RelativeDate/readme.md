# Meziantou.Framework.RelativeDate

`Meziantou.Framework.RelativeDate` allows to get a relative date similar to "5 minutes ago". It supports both local and UTC dates as well as dates with offset (DateTimeOffset). Also, culture to use can be specified explicitly. If it is not, current thread's current UI culture is used. It supports Dutch, English, French, German, Italian, Japanese, Korean, Portuguese, Simplified Chinese, Spanish and Turkish.

````c#
using System.Globalization;
using Meziantou.Framework;

DateTime dateTime = ...;
var relativeDate = RelativeDate.Get(dateTime).ToString();
var relativeDateInFrench = RelativeDate.Get(dateTime).ToString(format: null, CultureInfo.GetCultureInfo("fr-FR"));
````
