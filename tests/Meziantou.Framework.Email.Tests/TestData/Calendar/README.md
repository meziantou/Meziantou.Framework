# vCard and iCalendar test files

The files of this folder are written for these tests, under the MIT license of the repository. `make-calendar.js` writes
them: run `node make-calendar.js <directory>`.

| File | Content |
| ---- | ------- |
| `contacts-21.vcf` | Two cards of vCard 2.1: quoted-printable values in UTF-8, a soft line break, types without names |
| `contacts-30.vcf` | A card of vCard 3.0: escaped texts, a group, a photo in base64, an extension with a quoted parameter |
| `contacts-40.vcf` | A card of vCard 4.0: `PREF=1`, a quoted list of types, a parameter with a line break, a photo in a `data:` URI |
| `calendar.ics` | A time zone with its daylight saving time, a weekly event with `EXDATE`, `RDATE`, attendees and an alarm, a modified occurrence (`RECURRENCE-ID`), a monthly event with a duration, a yearly all-day event, a to-do |
| `meeting-request.ics` | An invitation (`METHOD:REQUEST`) with an organizer and a required and an optional attendee |
| `meeting-reply.ics` | The tentative reply (`METHOD:REPLY`) of an attendee, whose UID is a global object identifier of Outlook |

`CalendarTests` checks the values the library reads in these files, the round trips of the files the library writes,
and the occurrences of recurrence rules.

## python-dateutil

`dateutil-rrule.tsv` contains the recurrence rules of the tests of [python-dateutil](https://github.com/dateutil/dateutil),
tag [`2.9.0.post0`](https://github.com/dateutil/dateutil/blob/2.9.0.post0/tests/test_rrule.py), file
`tests/test_rrule.py`, under the Apache License 2.0 and the BSD 3-Clause License (see
[LICENSE-dateutil.txt](LICENSE-dateutil.txt)). Each line has the name of the test, the start, the rule and the
occurrences the test expects (`-` for none), separated by tabulations. `extract-dateutil-rrule.py` writes it from the test file, without
running it: run `python3 -I extract-dateutil-rrule.py test_rrule.py > dateutil-rrule.tsv`. It keeps the rules of the
frequencies and the parts the library supports: 62 of the 142 rules with a daily or longer frequency.

`CalendarTests.RecurrenceRule_MatchesDateutil` compares the occurrences with the ones dateutil expects. dateutil does not
count the start as an occurrence when the rule does not give it, unlike [RFC 5545] 3.8.5.3: the test takes it into account.
The rules whose occurrences differ are listed in the test, with the reason.

[RFC 5545]: https://www.rfc-editor.org/rfc/rfc5545
