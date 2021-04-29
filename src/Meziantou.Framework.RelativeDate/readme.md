# Meziantou.Framework.RelativeDate

`Meziantou.Framework.RelativeDate` allows to get a relative date similar to "5 minutes ago". It supports both local and UTC dates as well as dates with offset (DateTimeOffset). Also, culture to use can be specified explicitly. If it is not, current thread's current UI culture is used. It supports English and French.

````c#
using Meziantou.Framework;

DateTime dateTime = ...;
var relativeDate = RelativeDate.Get(dateTime).ToString();
var relativeDate = RelativeDate.Get(dateTime).ToString(format: null, CultureInfo.Get("fr-FR"));
````
