# Unix compress test data

The files of this folder were created for these tests with the `compress` command of macOS (`/usr/bin/compress`), and are
under the license of the repository.

- `sample-b9.Z`, `sample-b10.Z`, `sample-b11.Z`, `sample-b12.Z`, `sample-b16.Z`: `compress -b <bits> -c ../BZip2/sample.txt`.
  The codes grow from 9 bits to the maximum, and the decoder must skip the padding of the groups of codes at each change.
- `clear-b9.Z`: `compress -b 9 -c -f` of `ab` repeated 6000 times, then 24000 bytes `a` or `b` chosen by a linear
  congruential generator (`x = (x * 1103515245 + 12345) & 0x7FFFFFFF` from `x = 1`, the byte is `'a' + ((x >> 16) & 1)`). The
  compression ratio drops in the second part, so that compress clears the table (the code 256 of the block mode).
- `words-b16.Z`: `compress -b 16 -c -f` of 6000 words separated by spaces, chosen by the same generator (`words[(x >> 16) % 22]`,
  see `UnixCompressStreamTests`). The codes reach 13 bits.
