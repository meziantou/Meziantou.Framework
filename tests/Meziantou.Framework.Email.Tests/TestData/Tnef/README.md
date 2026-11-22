# TNEF test files

The files come from the test data of [Apache POI](https://github.com/apache/poi), commit
[`f669279`](https://github.com/apache/poi/tree/f66927954a29cf1f5b86614fdf1816e6321c8aea/test-data/hmef), folder
`test-data/hmef`, under the Apache License 2.0 (see [LICENSE-poi.txt](LICENSE-poi.txt)). They are copied unchanged; only
`poi-` is added before their names.

| File | Content |
| ---- | ------- |
| `poi-quick-winmail.dat` | A message with five attachments, in the attributes of the old versions of Microsoft Mail and in MAPI properties |
| `poi-bug52400-winmail-simple.dat` | A message with recipients, ended by a line break after its last attribute |

The other files of the folder are not copied: `winmail-sample1.dat` contains the personal bookmarks of someone, and
the larger files add nothing these files do not cover. The files of the `tnef` program are under the GPL.

`TnefFileTests` checks that these files, and the items of the .msg files, written as TNEF streams, read back the same.
