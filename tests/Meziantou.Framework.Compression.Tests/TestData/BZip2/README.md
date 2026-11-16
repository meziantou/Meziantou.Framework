# BZip2 test data

The files of this folder were created for these tests, and are under the license of the repository.

- `sample.txt`: 120 lines of random words (Python's `random.Random(42)`), 6137 bytes.
- `sample.txt.bz2`: `bzip2 -9 -c sample.txt` (bzip2 1.0.8, macOS).
- `multistream.bz2`: two bzip2 streams, concatenated as pbzip2 does: the first 3000 bytes of `sample.txt` (`bzip2 -9`), then the rest (`bzip2 -1`).
- `multiblock.bz2`: `bzip2 -1` of `abcdefghij` repeated 25000 times (250000 bytes, 3 blocks of 100 kB).
- `empty.bz2`: `bzip2 -9` of an empty input.
- `randomized.bz2`: `sample.txt` compressed by the encoder of this library with the randomized blocks of bzip2 0.9.0 (`BZip2EncoderStream` with `randomize: true`, blocks of 100 kB). `bzip2 -d` (1.0.8) decompresses it to `sample.txt`.
