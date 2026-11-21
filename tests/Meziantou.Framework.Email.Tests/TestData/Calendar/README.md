# vCard and iCalendar test files

The files of this folder are written for these tests, under the MIT license of the repository. `make-calendar.js` writes
them: run `node make-calendar.js <directory>`.

| File | Content |
| ---- | ------- |
| `contacts-21.vcf` | Two cards of vCard 2.1: quoted-printable values in UTF-8, a soft line break, types without names |
| `contacts-30.vcf` | A card of vCard 3.0: escaped texts, a group, a photo in base64, an extension with a quoted parameter |
| `contacts-40.vcf` | A card of vCard 4.0: `PREF=1`, a quoted list of types, a parameter with a line break, a photo in a `data:` URI |
| `calendar.ics` | A time zone with its daylight saving time, a weekly event with `EXDATE`, `RDATE`, attendees and an alarm, a modified occurrence (`RECURRENCE-ID`), a monthly event with a duration, a yearly all-day event, a to-do |

`CalendarTests` compares what the library reads with what [FolkerKinzel.VCards](https://github.com/FolkerKinzel/VCards)
and [Ical.Net](https://github.com/ical-org/ical.net) read, in these files and in the files the library writes, and
compares the occurrences of recurrence rules with the ones of Ical.Net.
