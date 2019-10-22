using System;

namespace Meziantou.Framework.Scheduling
{
    public sealed class CalendarUserAddress
    {
        // RFC2445 - 4.3.3 Calendar User Address

        public Uri Uri { get; }

        public CalendarUserAddress(string email)
        {
            Uri = new Uri("mailto:" + email);
        }

        public override string ToString()
        {
            return Uri.ToString();
        }
    }
}
