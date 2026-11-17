# Legacy zip methods test data

## From Apache Commons Compress

These files are test resources of Apache Commons Compress (https://github.com/apache/commons-compress, directory
`src/test/resources`), under the Apache License 2.0 (https://www.apache.org/licenses/LICENSE-2.0). They are unchanged.

- `SHRUNK.ZIP`: 2 entries compressed with the shrink method (method 1).
- `imploding-4Kdict-2trees.zip`: an entry compressed with the implode method (method 6), with a window of 4 KiB and no literal tree.
- `imploding-8Kdict-3trees.zip`: an entry compressed with the implode method, with a window of 8 KiB and a literal tree.

No sample of the reduce methods (methods 2 to 5) was found: the tests build reduced data by hand.

## Created for these tests

These files were created with 7-Zip 26.06 (`7zz a -tzip -mm=Deflate64 -mx=9`), and are under the license of the repository.

- `deflate64-text.zip`: `../BZip2/sample.txt`, compressed with Deflate64 (method 9).
- `deflate64-far.zip`: `far.bin`, 3 times 200 bytes of a linear congruential generator followed by 39800 zeros (120000
  bytes), so that the copies are 40000 bytes back: the distance codes 30 and 31 of Deflate64.
