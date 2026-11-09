# OpenEXR chunk compression — clean-room format specification

This document is the only artifact of the clean-room process behind the OpenEXR codec of this library: it was written by
reading the OpenEXR reference library (BSD-3-Clause, see `THIRD-PARTY-NOTICES.md`) and describes the bytes of the
compressed chunks, not the code that produces them. The codec was written from this document and from the OpenEXR file
layout specification, without access to the OpenEXR sources, and cites the sections below by number. Sections 8 (DWAA
and DWAB), 13 (ZSTD) and 14 (HTJ2K and LJ2K), and the deep-data, compositing and multi-part parts of section 1 (1.6 to
1.8), were added later from the same source version; the codec may not implement all of them yet.

## 0. Provenance and conventions

* Source studied: OpenEXR **v3.5.2**, commit `69b2604fc76e370615438bdc8d2cd95b9349c12e` (the commit the
  `v3.5.2` tag points to, committed 2026-10-01). In v3.5.x the C++ library delegates all chunk
  compression and decompression to the C "Core" library, so there is a single reference behaviour.
* This document describes the **data format** only. Wherever a behaviour is an encoder choice and not a
  requirement of the format, it is marked **[encoder choice]**. Behaviour that only matters for invalid input is marked
  **[decoder robustness]**.
* Notation:
  * `u8`, `u16`, `u32`, `u64`: unsigned integers of that bit width. `i16`: signed 16-bit two's complement.
  * `LE` = little-endian, `BE` = big-endian.
  * `x mod 2^k` always means the non-negative remainder (equivalently, keeping the low k bits of the two's-complement
    representation).
  * `trunc16(v)` = the integer v reduced to 16 bits and reinterpreted as `i16` (two's-complement wrap).
  * `floor(v/2)` for a signed v is an arithmetic right shift by 1 (rounds toward minus infinity).
  * Bit strings are written MSB first.
* All multi-byte numbers stored in the file are little-endian unless explicitly stated otherwise (B44 is the notable
  exception for its internal block fields; the Huffman bit stream is MSB-first).

Compression identifiers (the `compression` header attribute, a single byte):

| id | name | lines per scan-line chunk | lossy |
|---|---|---|---|
| 0 | NONE | 1 | no |
| 1 | RLE | 1 | no |
| 2 | ZIPS | 1 | no |
| 3 | ZIP | 16 | no |
| 4 | PIZ | 32 | no |
| 5 | PXR24 | 16 | yes (FLOAT only) |
| 6 | B44 | 32 | yes (HALF only) |
| 7 | B44A | 32 | yes (HALF only) |
| 8 | DWAA | 32 | yes |
| 9 | DWAB | 256 | yes |
| 10 | HTJ2K256 | 256 | no |
| 11 | HTJ2K32 | 32 | no |
| 12 | LJ2K | 256 | yes (R, G, B only) |
| 13 | ZSTD | 1 | no |

The lines-per-chunk values are confirmed exactly as listed above for every id, including 10–13 (checked against the
reference's lines-per-chunk function and against each compressor). HTJ2K256 and HTJ2K32 are new in v3.4, LJ2K and ZSTD
in v3.5.0; they are described in sections 14 and 13. Any id ≥ 14 is invalid. Deep parts allow only 0, 1, 2 and 13
(1.6).

---

## 1. Common: the uncompressed chunk ("packed") layout

Every compressor transforms one **uncompressed chunk buffer** (below: U) into the stored bytes and back. The
decoder must know the exact size and layout of U, which are determined entirely by the header.

### 1.1 Sample encoding

| pixel type | id | bytes per sample | encoding in U |
|---|---|---|---|
| UINT | 0 | 4 | u32 LE |
| HALF | 1 | 2 | IEEE 754 binary16 bit pattern, u16 LE |
| FLOAT | 2 | 4 | IEEE 754 binary32 bit pattern, u32 LE |

### 1.2 Channel order and sampling

* Channels appear in **header order** (the channel list attribute, which is sorted by name, byte-wise ascending).
* Each channel has `xSampling ≥ 1`, `ySampling ≥ 1`. A valid file satisfies, for every channel:
  `dataWindow.min.x mod xSampling = 0`, `dataWindow.min.y mod ySampling = 0`,
  `width mod xSampling = 0`, `height mod ySampling = 0` (width/height of the data window). Tiled and deep parts
  require `xSampling = ySampling = 1`.
* A channel **has a sample line at absolute y** if and only if `y mod ySampling = 0` (mathematical modulo; negative y is
  allowed, for example y = −4 with ySampling 2 has a line, y = −3 does not).
* On a line where it is present, a channel has `width / xSampling` samples (width of the chunk, which for scan-line
  parts is always the full data-window width).

### 1.3 Scan-line chunks

* Chunk k (k = 0, 1, …) starts at `y0 = dataWindow.min.y + k × L`, L = lines per chunk from the table above.
  Chunks are aligned to the data-window top, not to y = 0.
* The chunk covers lines `y0 … min(y0 + L − 1, dataWindow.max.y)`; therefore **only the last chunk can be shorter**.
  Number of chunks = `ceil(height / L)`.
* Layout of U: for each line y of the chunk in **increasing y** (line order `DECREASING_Y` changes only the order of
  chunks in the file, never the order of lines inside a chunk), for each channel in header order, if the channel has a
  sample line at y, its `width / xSampling` samples left to right. Nothing else (no padding, no alignment).
* Size of U = Σ over channels of `bytesPerSample × (width / xSampling) × n_c`, where `n_c` = number of y in the chunk
  with `y mod ySampling = 0`. `n_c` may be 0 for a sub-sampled channel in a short chunk (for example, ySampling 32 or a
  chunk of a single line that is not a multiple of ySampling); such a channel contributes nothing to that chunk.

### 1.4 Tiled chunks

* One chunk = one tile (of one level). "Lines per chunk" does not apply. Sampling is always 1.
* A tile at the right/bottom edge of a level is clipped to the level size, so its width/height can be smaller than
  the tile size.
* Layout of U: for each row of the tile top to bottom, for each channel in header order, the tile-width samples.
  Size = tileW × tileH × Σ bytesPerSample.
* Every compressor below applies to a tile exactly as to a scan-line chunk whose width is the tile width and whose line
  count is the tile height (with all ySampling = 1, the "which lines have a channel" logic is trivial).

### 1.5 The raw-storage rule (applies to every compression id ≠ NONE)

* **Decoder:** let P = stored data size of the chunk (the size field of the chunk) and S = size of U computed from the
  header (section 1.3/1.4). If **P = S**, the stored bytes **are U, uncompressed**, whatever the compression id; the
  decompressor must not be run. If P ≠ S (P < S normally, P > S is tolerated) the decompressor runs. If P = 0 the
  reference returns without producing data **[decoder robustness: treat as corrupt or zero-fill]**.
* **Encoder:** when the compressed result is **not strictly smaller** than S, write U uncompressed (P = S).
  Using "not strictly smaller" (≥) is mandatory for correctness: a compressed payload of size exactly S would be
  misread as raw.

  Reference behaviour per compressor (informative): RLE, ZIP/ZIPS, DWA fall back to raw when compressed ≥ S; PIZ
  stores compressed only when compressed < S. PXR24 and B44/B44A in the Core library fall back only when compressed
  **>** S, so a Core-only writer can emit a compressed payload of exactly S bytes that every reader then misreads as raw
  (example: one HALF channel, B44 non-A, width 7, a 2-line chunk: 2 blocks × 14 = 28 = 7 × 2 × 2). The C++ writer adds
  its own "use compressed only if strictly smaller" check, so files from the C++ API are safe. Our encoder must use ≥.

### 1.6 Deep data

**Allowed compressions.** A deep part (deep scan line or deep tiled) may only use NONE (0), RLE (1), ZIPS (2) or
ZSTD (13). Any other id in a deep part is rejected by the reference when the header is validated. Deep parts always
have `xSampling = ySampling = 1`. Lines per chunk is 1 for every allowed compression, so a deep scan-line chunk is a
single line; a deep tiled chunk is one tile (clipped at the right/bottom edges of the level, as in 1.4).

**Chunk layout** (the container is in the File Layout documentation; repeated here for the sizes the codec uses).
After the optional part number (multi-part files only) and the line y (deep scan line) or the four i32 tile
coordinates (deep tiled), a deep chunk has three **u64 LE** fields, then two byte blocks:

| field | meaning |
|---|---|
| Pt | stored (packed) size of the sample count table |
| Pd | stored (packed) size of the sample data |
| Sd | uncompressed size of the sample data |
| Pt bytes | the stored sample count table |
| Pd bytes | the stored sample data |

**Uncompressed sample count table.** `w × h` entries (w, h = width and height of the chunk: the data-window width and
1 line for a deep scan-line chunk; the clipped tile size for a tile), each an **i32 LE**, row by row, left to right.
The entries are **cumulative within each row**: the entry of pixel x is the total number of samples of the pixels 0…x
of that row; the count restarts at 0 at every row (the count of a pixel is its entry minus the entry of its left
neighbour, or the entry itself for x = 0). Its uncompressed size is `St = w × h × 4`, with w and h the **clipped** chunk
size (deep scan line: `St = dataWindowWidth × 4`); the row stride is the clipped width w. The reference rejects a row
whose entries decrease (invalid sample data), and a chunk whose total sample count times the sum of the channels' bytes
per sample exceeds Sd. The last entry of a row is that row's sample total `n_row`.

**Uncompressed sample data.** For each row of the chunk top to bottom, for each channel in header order: the `n_row`
samples of that channel on that row, pixel by pixel, the samples of one pixel in their stored order (front to back as
written; the format does not require any order). Samples are encoded as in 1.1 (u32 / binary16 / binary32 LE). This is
channel-planar per row, never pixel-interleaved. Size = Σ over rows of `n_row × Σ bytesPerSample`, which must equal Sd.

**Compression.** The table and the data are compressed **independently**, each with the part's compression, as if
each were a separate chunk buffer U:

* The raw rule of 1.5 applies to each one separately: `Pt = St` means the table is stored raw, `Pd = Sd` means the
  data are stored raw. A compressed table (RLE, ZIPS, ZSTD) must decompress to exactly `St` bytes (clipped size).
* **Deep scan lines:** the reference requires `Pt = St = width × 4` exactly with NONE.
* **Deep tiles stored raw — the full-tile quirk.** The reference C++ writer (DeepTiledOutputFile) keeps one table buffer
  of the **unclipped** tile size `T = tileXSize × tileYSize × 4` per tile buffer. It writes the clipped table at the start
  of that buffer (row stride = clipped width w, `w × h` entries), and when it stores the table raw (always with NONE;
  with a compression, when the compressed table is **not smaller than T**) it writes **Pt = T** bytes: the clipped table
  followed by `T − St` bytes that are whatever the reused buffer holds (zeros for the first tile written through that
  buffer, stale entries of an earlier tile otherwise — never meaningful). This applies to a tile clipped in x, in y, or
  both. Readers must therefore accept, for a deep tile:
  * with NONE: any `Pt ≥ St`; use the first St bytes (row stride w) and ignore the rest. The reference reader also
    accepts `Pt < St` and zero-fills the missing entries **[decoder robustness]**; recommended: reject `Pt < St`.
  * with RLE/ZIPS/ZSTD: `Pt = St` → raw; otherwise decompress to exactly St. The reference reader has **no** case for
    `Pt = T ≠ St`: it runs the decompressor on the raw bytes and fails. Such a chunk (a clipped tile whose table did not
    compress below T) is therefore written by the reference but unreadable by it; recommended: also treat `Pt = T`
    (unclipped size, with `T ≠ St`) as raw, using the first St bytes. This is unambiguous: the reference never stores a
    compressed table of T bytes or more (it falls back to raw at ≥ T).
  * Writer-side consequence of comparing against T instead of St: a compressed table whose size happens to equal St
    is stored compressed and then misread as raw by every reader. A Core-only writer (no C++ layer) uses St everywhere
    (raw table of exactly St bytes, compressed only when smaller).
* **Our encoder:** write `Pt = St` (clipped) when the table is raw, and store it compressed only when strictly smaller
  than St. This is readable by the reference reader for every compression.
* **RLE**: section 3 (section 2 transform + run-length bytes), applied to the table bytes and to the data bytes.
* **ZIPS**: section 4 (section 2 transform + zlib).
* **ZSTD**: section 13, with the two deep layouts described in 13.4 (the table is one 4-byte channel; the data use
  the per-row totals as the sample count of every channel).
* An empty data block (`Sd = 0`, `Pd = 0`) is valid (no samples in the chunk). **[decoder robustness]** A compressed
  part with `Pt = 0` is read by the reference as an all-zero table.
* Reference writer **[encoder choice]**: table and data each stored compressed only when strictly smaller than their
  uncompressed size (the C++ writer applies this check to both). Our encoder must use the same rule.

### 1.7 Flattening deep pixels (OpenEXR's compositing API)

OpenEXR's flattening (the deep-to-flat compositing helper of the C++ library, with the default compositing
behaviour; v3.5.2) works on deep **scan-line** sources only and is not part of the file format. Its exact behaviour:

**Inputs and channels.**
* Every source (file or part) must have a channel named exactly `Z` and one named exactly `A` (no layer prefix,
  case-sensitive), otherwise it is rejected. All sources must have the same display window; the output data window is
  the union of the sources' data windows. A source contributes no samples to pixels outside its data window.
* If **any** source has a channel named exactly `ZBack`, a ZBack channel is used for all sources; a source without it
  then reads it as the deep fill value 0.0. If no source has `ZBack`, the ZBack values are the Z values (Z is used
  twice).
* The other composited channels are the channels the caller asks for in the output frame buffer (by name); a source
  that lacks a requested channel reads it as 0.0 for every sample. Output channels must not be sub-sampled.
* All sample values are read as **binary32**, converted from the stored type with the reader conversions of section
  10 (HALF exact, UINT rounded to nearest binary32).

**Gathering and sorting.** For a pixel, the samples of all sources are concatenated: first the sources added as
files, in the order they were added, then the sources added as parts, in the order they were added (whatever the
interleaving of the add calls); within a source, the stored sample order. Let `nsrc` = number of sources that have at
least one sample at that pixel.
* If `nsrc ≤ 1`, the samples are **not sorted**: they are composited in stored order (a single source is assumed to be
  already sorted front to back).
* If `nsrc > 1`, the samples are sorted by the key (Z ascending, then ZBack ascending, then position in the
  concatenation ascending). The position tie-break makes the order fully deterministic (a stable sort is equivalent).
  **[decoder robustness]** NaN keys make the comparison an invalid ordering; the reference result is then whatever its
  sort algorithm produces. Recommended: treat NaN as never smaller nor larger (the comparator above then falls through
  to the next key), and document it as unspecified.

**Compositing.** Outputs for every composited channel (Z, ZBack, A and every other requested channel) start at +0.0
(binary32). Then, for each sample s in the order above:
1. `α = A_out` (the accumulated alpha **before** this sample);
2. if `α ≥ 1.0`, stop (later samples are ignored, the comparison is on binary32);
3. for every channel c (including Z, ZBack and A themselves): `out_c = out_c + (1.0 − α) × in_c[s]`, where
   `1.0 − α` is rounded to binary32, the product is rounded to binary32 and the sum is rounded to binary32 (no fused
   multiply-add, no double precision). The α used for every channel of the sample is the value read in step 1.

So the composited Z and ZBack are alpha-weighted sums, not the depth of the front sample. Overlapping or volumetric
samples (Z < ZBack) are **not split** or merged; each sample is used whole, in sort order. There is no other threshold
than `α ≥ 1`.

**Pixels with no samples** get 0.0 for every channel (including A and Z).

**Output conversion.** FLOAT output slices receive the binary32 value as is; HALF output slices receive it rounded to
binary16 (section 10: nearest, ties to even, overflow to ±Inf); **UINT output slices are left unwritten** by the
reference (whatever the buffer held stays).

### 1.8 Multi-part files

* **Compression is per part and works exactly as in a single-part file.** No compressor reads anything outside its
  own part (the chunk payload depends only on that part's header and the chunk's position), and nothing in any
  compressed payload refers to the part number.
* Rules a reader needs that the File Layout documentation states only implicitly:
  * The offset tables follow the end-of-headers marker (the empty header, a single 0 byte after the last header), one
    table per part, **in part (header) order**, each with exactly that part's chunk count entries, back to back.
  * The chunk count of a part is **computed from its header** (data window, tiling, levels, lines per chunk). The
    `chunkCount` attribute is required by the specification for multi-part and deep files, but the reference only
    insists on it in its strict header mode; when it disagrees with the computed count, the reference warns and uses
    the computed count to size the offset table. Recommended: compute the count; reject the file (or warn) on a
    mismatch, never trust the attribute to locate the next table.
  * Every chunk starts with an i32 LE part number; the reference rejects a chunk whose part number differs from the
    index of the part whose offset table pointed to it. Chunks of different parts may be interleaved in any order in
    the file.

### 1.9 Edge cases to test (common)

* Data window with negative `min.y`, sub-sampled channels (ySampling 2, 3) whose first line is negative.
* Last chunk shorter than L (height not a multiple of 16/32/256), including a last chunk of 1 line.
* A chunk in which a sub-sampled channel has no line at all (n_c = 0), for each compressor.
* Width 1 and height 1 images.
* A stored chunk whose size equals S (must be read as raw for every compression id).
* Channel names that sort differently from insertion order (layout follows sorted order).

---

## 2. Byte reordering and delta predictor (shared by RLE and ZIP/ZIPS, also used inside DWA)

Input: the n bytes `u[0..n−1]` of U. Output: n bytes `v[0..n−1]` that are then entropy-coded.

**Step 1 — split.** Let `h = ceil(n/2)`. Build `t` of length n:
* `t[k] = u[2k]` for `0 ≤ k < h` (bytes at even offsets, in order),
* `t[h + k] = u[2k + 1]` for `0 ≤ k < floor(n/2)` (bytes at odd offsets, in order).

This is a split by byte position in U (even/odd offsets), not per sample or per channel. With odd n the first half is
one byte longer.

**Step 2 — predict.** `v[0] = t[0]` (stored unchanged; no +128 bias), and for `i ≥ 1`:
`v[i] = (t[i] − t[i−1] + 128) mod 256`.
The prediction runs straight across the boundary between the two halves (`v[h]` is predicted from `t[h−1]`).

**Inverse (decoder).** `t[0] = v[0]`; `t[i] = (t[i−1] + v[i] − 128) mod 256` for `i ≥ 1`; then
`u[2k] = t[k]`, `u[2k+1] = t[h+k]`.

All arithmetic is modulo 256 (bytes treated as unsigned; the sign interpretation does not matter).

Edge cases: n = 0, n = 1 (output is the single byte unchanged), n = 2, odd n, all-equal bytes (v = first byte followed
by 128s).

---

## 3. RLE (id 1)

Pipeline: encoder = section 2 transform of U, then the byte run-length coder below. Decoder = run-length decode to
exactly S bytes, then inverse section 2.

### 3.1 Run-length byte format

The stream is a sequence of packets. Each packet starts with a **signed** count byte c (two's complement, −128…127):

| c | meaning | packet size |
|---|---|---|
| 0 … 127 | repeat run: the next byte is written `c + 1` times (1…128 copies) | 2 bytes |
| −128 … −1 | literal: the next `−c` bytes (1…128) are copied verbatim | 1 + (−c) bytes |

### 3.2 Decoder rules

* Decode packets until the input is exhausted. The output must be **exactly** S bytes.
* Errors: a packet that would write past S bytes; a literal whose bytes extend past the end of the input; a repeat
  packet missing its value byte; total output ≠ S. (Because every packet produces ≥ 1 byte, extra trailing packets
  necessarily overflow S and are errors.)

### 3.3 Reference encoder **[encoder choice]**

Only packet lengths the decoder accepts are required. The reference: at the current position count how many following
bytes equal the current byte, up to 127 additional bytes; if there are at least 2 additional equal bytes (a run of
≥ 3), emit a repeat packet with `c = additional` (runs of 3…128 bytes). Otherwise emit a literal: extend it while the
next three bytes are not all equal and the literal is shorter than 127 bytes (literals never exceed 127 bytes even
though the decoder accepts 128). Then fall back to raw if the output is ≥ S (section 1.5).

Edge cases: S = 1; runs of exactly 127/128/129 equal bytes; alternating bytes (literal of 127 then a new literal);
literal count −128 (decoder must accept); a repeat packet with c = 0 (run of 1, decoder must accept);
truncated streams.

---

## 4. ZIPS (id 2) and ZIP (id 3)

* Byte format: the section 2 transform of U, compressed as a single **zlib stream (RFC 1950)**: 2-byte zlib header,
  raw DEFLATE (RFC 1951) data, 4-byte big-endian Adler-32 of the uncompressed (transformed) data. No preset dictionary
  (FDICT must be 0). No extra framing; there is no stored length.
* ZIPS and ZIP have an identical byte format; they differ only in lines per chunk (1 vs 16).
* Decoder requirements: the inflated size must equal S **exactly**, and the zlib stream must consume **all** P stored
  bytes (trailing bytes after the zlib stream are rejected by the reference). Adler-32 mismatch is an error.
* Compression level **[encoder choice]**: not stored in the file. The v3.5 reference uses libdeflate at level 4 when no
  level is configured (older versions used zlib level 6). Any valid zlib stream is accepted, including stored/fixed/dynamic
  blocks; the zlib header's FLEVEL field is informational.
* Fallback: raw when the zlib output is ≥ S.

Edge cases: S below 16 bytes (the zlib overhead alone is 6 bytes so very small chunks usually fall back to raw);
incompressible noise (must fall back to raw); chunk where inflate yields S − 1 or S + 1 bytes (must be an error).

---

## 5. PIZ (id 4)

PIZ is lossless for all three pixel types. Overview of the encoder:

1. Rearrange U into per-channel planes of 16-bit words (5.1).
2. Range-compress: build a bitmap of the 16-bit values that occur, replace every word by its index in the sorted set
   of occurring values (5.2).
3. Apply a 2D Haar-like wavelet independently to every word plane (5.3).
4. Huffman-code the whole word buffer (5.4).
5. Write the chunk (5.5).

The decoder does the inverse in reverse order: read bitmap, Huffman-decode, inverse wavelet, reverse lookup,
re-interleave into U.

### 5.1 Planar 16-bit word buffer W

* For each channel c in header order, define its plane: `nx = width / xSampling`, `ny = n_c` (number of lines of that
  channel inside the chunk, section 1.3), and `w = bytesPerSample / 2` words per sample (HALF: w = 1; FLOAT and
  UINT: w = 2).
* W is the concatenation, in header order, of each channel's block of `nx × ny × w` u16 words. Inside a channel's
  block, the sample lines are stored top to bottom (the k-th line of that channel present in the chunk is row k), each
  row `nx` samples left to right, each sample as its w words.
* Word values: each sample's bytes as they appear in U, read as consecutive **u16 LE** words. For a 32-bit sample with
  bytes b0 b1 b2 b3: word 0 = b0 | b1<<8 (the low half of the u32 value), word 1 = b2 | b3<<8 (the high half). The two
  words of a sample are adjacent (interleaved), they are not split into separate planes in memory.
* Channels with `ny = 0` or `nx = 0` contribute nothing.
* Total word count N = S / 2.

### 5.2 Range compression (bitmap and lookup tables)

**Bitmap.** An 8192-byte bitmap B (65536 bits): bit v (byte `v >> 3`, bit `v & 7`, bit 0 = least significant) is set iff
the value v occurs in W. Then **bit 0 (value 0) is cleared**: zero is always treated as present and never stored.

**firstByte / lastByte.** The lowest and highest byte indices i (0…8191) with `B[i] ≠ 0`. If no byte is non-zero,
the encoder writes `firstByte = 8191` and `lastByte = 0` (so min > max). (Any min > max means "empty bitmap" to the
decoder.)

**Forward table (encoder).** Walk v = 0 … 65535 upward; maintain a counter k starting at 0. If v = 0 **or** bit v of B
is set, then `forward[v] = k` and k increases by 1; otherwise `forward[v] = 0` (never used).
`maxIndex = k − 1` (the number of distinct values including zero, minus one). Every word of W is replaced by
`forward[word]`. Value 0 always maps to 0.

**Reverse table (decoder).** Walk v = 0 … 65535 upward; k starts at 0; if v = 0 or bit v of B is set, `reverse[k] = v`,
k++. `maxIndex = k − 1`. Remaining entries are 0. The decoder treats value 0 as present whether or not bit 0 is set in
the stored bitmap.

**14-bit versus 16-bit wavelet.** If `maxIndex < 16384` (2^14) the 14-bit wavelet variant is used for **all** planes of
the chunk; otherwise the 16-bit variant. The decision depends only on the bitmap, so the decoder recomputes it.

### 5.3 Wavelet transform

Applied independently to each **word plane**: for a channel with w = 1 the plane is its block of nx × ny words; for
w = 2 there are two planes, plane j ∈ {0, 1} consisting of word j of every sample (stride 2 words horizontally,
`2 × nx` words vertically). Below, a plane is an array `A[x, y]`, 0 ≤ x < nx, 0 ≤ y < ny.

#### 5.3.1 Pair transforms

**14-bit forward** `(a, b) → (l, h)`, a and b interpreted as i16:
`l = trunc16(floor((a + b) / 2))`, `h = trunc16(a − b)`.

**14-bit inverse** `(l, h) → (a, b)`, l and h interpreted as i16:
`a = trunc16(l + ceil(h / 2))`, `b = trunc16(a_full − h)` where `a_full = l + ceil(h/2)` before truncation. Note
`ceil(h/2) = (h mod 2) + floor(h/2)` for negative h too (for example h = −3 → 1 + (−2) = −1).
Exact inverse of the forward transform: `a = l + ceil(h/2)`, `b = a − h`.

**16-bit forward** `(a, b) → (m, d)`, a and b unsigned u16, intermediate values in ordinary integers:
1. `a' = (a + 32768) mod 65536`
2. `m = floor((a' + b) / 2)` (a' + b ≥ 0, so a plain halving)
3. `d = a' − b` (range −65535 … 65535)
4. if `d < 0`: `m = (m + 32768) mod 65536`
5. `d = d mod 65536`

**16-bit inverse** `(m, d) → (a, b)`, m and d unsigned:
`b = (m − floor(d / 2)) mod 65536`, `a = (d + b − 32768) mod 65536`.

#### 5.3.2 2D quad transform

For a quad of four positions `P00 = (x, y)`, `P10 = (x + p, y)`, `P01 = (x, y + p)`, `P11 = (x + p, y + p)`
and a pair transform F (14- or 16-bit forward):
1. `(L1, H1) = F(A[P00], A[P10])` (horizontal, top row)
2. `(L2, H2) = F(A[P01], A[P11])` (horizontal, bottom row)
3. `(A[P00], A[P01]) = F(L1, L2)` (vertical on the low parts)
4. `(A[P10], A[P11]) = F(H1, H2)` (vertical on the high parts)

Inverse with the inverse pair transform G:
1. `(L1, L2) = G(A[P00], A[P01])`
2. `(H1, H2) = G(A[P10], A[P11])`
3. `(A[P00], A[P10]) = G(L1, H1)`
4. `(A[P01], A[P11]) = G(L2, H2)`

#### 5.3.3 Levels and traversal

Let `n = min(nx, ny)`. The level step p takes the values 1, 2, 4, … for every p with `2p ≤ n`. If n < 2 (width or
height of the plane is 0 or 1) the plane is left untouched.
* **Encoding** processes the levels with **increasing** p; **decoding** with **decreasing** p (starting from the
  largest power of two p such that 2p ≤ n).

At a level with step p (write P = 2p):

1. **Quads.** For every y in {0, P, 2P, …} with `y ≤ ny − P`, and every x in {0, P, 2P, …} with `x ≤ nx − P`: apply
   the quad transform at (x, y) with step p.
2. **Odd column.** Let `xe = P × floor(nx / P)`. If the bit p is set in nx (`nx AND p ≠ 0`, bitwise), then for every y
   of step 1 apply the pair transform vertically: `(A[xe, y], A[xe, y + p]) = F(A[xe, y], A[xe, y + p])`.
3. **Odd row.** Let `ye = P × floor(ny / P)`. If `ny AND p ≠ 0`, then for every x of step 1 apply the pair transform
   horizontally: `(A[x, ye], A[x + p, ye]) = F(A[x, ye], A[x + p, ye])`.

The corner position (xe, ye) is never transformed at that level. The three groups touch disjoint positions, so their
order inside a level does not matter; for decoding, use the inverse G in the same places.

Important: the odd-column / odd-row condition is the **bit test `nx AND p` / `ny AND p`**, not "the number of positions
at this level is odd". Example: nx = 5 at p = 2: positions 0, 2, 4 exist, but 5 AND 2 = 0, so column 4 is not
transformed at that level. nx = 6 at p = 2: 6 AND 2 = 2, so column 4 is transformed vertically. Bit-exactness requires
this exact rule.

#### 5.3.4 Overflow notes

* With the 14-bit variant and input values < 2^14 (guaranteed after range compression), every intermediate value fits
  in i16, so `trunc16` never actually wraps for valid data.
* **[decoder robustness]** The v3.5 Core 14-bit decoder computes the four steps of a quad inverse in full integers and
  truncates only the final four values, while the classic definition truncates after each pair step. These agree for
  every stream produced from valid data and only differ for corrupt data. Prefer the per-pair truncation as described
  in 5.3.1; it is the classic definition and matches for all valid files.

### 5.4 Huffman coding of W

The whole buffer W (all channels, after the forward table and the wavelet, in memory order) is coded as one Huffman
stream of N 16-bit symbols.

#### 5.4.1 Stream layout

| offset | size | field |
|---|---|---|
| 0 | u32 LE | `im` — smallest symbol with a code (smallest value present in the data) |
| 4 | u32 LE | `iM` — largest symbol with a code; **this symbol is the run-length symbol** |
| 8 | u32 LE | `tableLength` — byte length of the packed code-length table |
| 12 | u32 LE | `nBits` — number of meaningful bits in the encoded data |
| 16 | u32 LE | reserved, write 0 (ignored by the reader) |
| 20 | tableLength bytes | packed code-length table (5.4.2) |
| 20 + tableLength | ceil(nBits / 8) bytes | encoded symbols (5.4.4) |

Total length = 20 + tableLength + ceil(nBits/8). The decoder does not use `tableLength`: it parses the table and
starts the data at the first byte after the table's last (padded) byte; the encoder must still write the correct value.

Symbol alphabet: 0 … 65536 (65537 symbols). Data values are 0 … 65535; the run-length symbol is `iM`, which the
reference encoder sets to (largest data value present) + 1, so it can be 65536. **Requirements:** `im ≤ iM ≤ 65536`;
every data value lies in `[im, iM − 1]`; the symbol `iM` is never used as a literal (the decoder always interprets
`iM` as the run code). The reference always gives the run symbol a code (it is counted with frequency 1), even if no run
is emitted; recommended for compatibility.

#### 5.4.2 Packed code-length table

Bit-packed MSB-first (the first field occupies the high bits of the first byte), describing the code length of every
symbol from `im` to `iM` inclusive, in increasing symbol order. Each entry starts with a 6-bit value v:

| v | meaning |
|---|---|
| 0 | symbol has no code (length 0) — a single zero |
| 1 … 58 | code length v |
| 59, 60, 61, 62 | a run of `v − 57` consecutive zero lengths (2, 3, 4, 5 symbols) |
| 63 | followed by an 8-bit value r: a run of `r + 6` zero lengths (6 … 261 symbols) |

After the last entry the table is padded with 0 bits to a byte boundary. Errors: a zero run extending past `iM`;
running out of input while reading the table.

Reference encoder **[encoder choice]**: a single zero is written as 0; runs of 2…5 zeros use 59…62; runs of 6…261 use
63 + (n − 6); longer zero runs are split greedily (261 then the remainder).

#### 5.4.3 Canonical code assignment

Code values are derived from the lengths alone:
1. `count[L]` = number of symbols with length L, for L = 1 … 58.
2. Starting codes, from the **longest** length down: `start[58] = 0`, and for L = 58 down to 2:
   `start[L − 1] = floor((start[L] + count[L]) / 2)`.
3. Visit symbols in **increasing symbol order**; a symbol of length L gets code `start[L]` and `start[L]` is
   incremented.

So, read as integers left-aligned to the same width, **longer codes are numerically smaller**, and within one length
codes increase with the symbol value. Example: lengths {sym0: 1, sym1: 2, sym2: 2} → sym1 = `00`, sym2 = `01`,
sym0 = `1`.

Constraints a valid stream must satisfy (both reference decoders depend on them):
* Maximum code length 58. **Recommended: ≤ 57**, because one of the two reference decoders (the "fast" one used on
  x86 and Apple platforms when nBits > 128) does not count length-58 codes and rejects such tables.
* The code must be **complete**: Σ 2^(−L) over coded symbols = 1 exactly (a full binary tree). Over-subscribed codes are
  rejected; incomplete codes are decoded inconsistently between the two reference decoders (one uses floor in step 2,
  the other an equivalent ceiling formula; they coincide only for complete codes). A Huffman tree built with ≥ 2 leaves
  is always complete. With the run symbol always present there are always ≥ 2 coded symbols.
* How lengths are chosen is an **[encoder choice]** (the reference uses a classic Huffman merge with a binary heap,
  ties broken by frequency then by position; reproducing it is not needed for compatibility). A length-limited
  algorithm (for example package-merge) producing a complete code is fine.

#### 5.4.4 Encoded data

* Bits are emitted MSB-first: each code's bits from its most significant bit, packed into bytes from bit 7 down to
  bit 0. The final partial byte is padded with **0 bits** in its low positions.
* `nBits` must be the exact number of meaningful bits (no padding counted).
* Symbols: walk W from the start. A literal value v is emitted as `code(v)`. A **run** is coded as
  `code(v)` followed by `code(iM)` followed by an **8-bit repeat count r** (MSB-first, 1…255), meaning: after the
  explicit v, v is repeated r more times (r + 1 copies in total). The repeat count copies the **last output symbol**,
  whatever produced it.
* Reference encoder choice **[encoder choice]**: consecutive equal values are grouped in runs of at most 256 (one
  literal + up to 255 repeats); a group of `1 + r` copies is emitted as the run form only when
  `len(v) + len(iM) + 8 < len(v) × r`, otherwise as `r + 1` literal codes. The encoder never emits r = 0.

Decoder requirements and errors:
* Output exactly N symbols. Fewer symbols at the end of the data, or extra codes after N symbols, are errors.
* A run code with no previous symbol (first symbol) is an error. A repeat that would exceed N is an error.
* A repeat count of 0 is rejected by the fast reference decoder (accepted as a no-op by the other): never emit it.
* A bit pattern that matches no code is an error. Codes that would extend past nBits are errors.
* The fast reference decoder additionally requires that no meaningful input remains once N symbols are produced
  (trailing garbage is an error) — satisfied when nBits is exact.
* If the Huffman stream (as delimited by the PIZ length field) is shorter than 20 bytes: valid only if N = 0.
* `im` or `iM` ≥ 65537 → error. `20 + tableBytes + ceil(nBits/8)` beyond the available bytes → error.

If N = 0 the reference encoder produces an empty Huffman stream (0 bytes).

#### 5.4.5 Worked examples (as produced by the reference encoder)

* W = four zeros. Symbols: 0 (frequency 4) and run symbol 1; both get length 1; canonical codes: symbol 0 = `0`,
  symbol 1 = `1`. Table: entries 1, 1 → bits `000001 000001` + 4 padding bits → `04 10`. A run of 4 is not worth
  coding (1 + 1 + 8 is not < 1 × 3), so four `0` codes: nBits = 4, data `00`. Stream (23 bytes):
  `00 00 00 00 | 01 00 00 00 | 02 00 00 00 | 04 00 00 00 | 00 00 00 00 | 04 10 | 00`.
* W = 300 zeros. Same table. Groups: one literal + 255 repeats, then one literal + 43 repeats:
  `0 1 11111111 0 1 00101011` → nBits = 20, data `7F D2 B0`.

### 5.5 PIZ chunk layout

| field | size |
|---|---|
| firstByte | u16 LE |
| lastByte | u16 LE |
| bitmap bytes `B[firstByte … lastByte]` | `lastByte − firstByte + 1` bytes, present only if firstByte ≤ lastByte |
| hufStreamLength | u32 LE: total byte length of the Huffman stream (5.4.1) |
| Huffman stream | hufStreamLength bytes |

Decoder checks: P ≥ 4; `lastByte < 8192` (else error); bitmap bytes and the 4-byte length must fit in P;
`hufStreamLength` must fit in the remaining bytes (bytes after the Huffman stream are ignored). Bitmap bytes outside
[min, max] are zero. After the inverse wavelet and reverse lookup, the planes are re-interleaved into U (exact
inverse of 5.1), words written as u16 LE. Output must be exactly S bytes.

Fallback: the encoder writes U raw unless the PIZ output is strictly smaller than S (and the reference also falls back
to raw if its output buffer would overflow).

### 5.6 PIZ edge cases to test

* All-zero chunk: bitmap empty (min 8191, max 0 → no bitmap bytes), maxIndex 0, Huffman alphabet {0, run symbol 1}.
* Chunk where the only non-zero values are 1…7 (min = max = 0, one bitmap byte).
* A value of 65535 present (run symbol = 65536).
* Exactly 16384 distinct values (maxIndex 16383 → 14-bit) vs 16385 (maxIndex 16384 → 16-bit). FLOAT/UINT channels
  with random data almost always use the 16-bit path; HALF images usually the 14-bit path.
* Plane sizes: 1×N and N×1 (no wavelet), 2×2, 3×3, 5×5, 5×6, 6×5, 7×3, nx = 5 vs nx = 6 (odd-column bit rule), sizes
  with several set bits (for example 13, 27).
* Mixed channels (HALF + FLOAT + UINT) in one chunk; a sub-sampled channel absent from a chunk.
* Long runs (> 256 equal symbols, runs of exactly 255/256/257), run decisions with short codes (r ≈ 10).
* A table needing a long zero run (> 261 absent symbols between present ones).
* Huffman data with nBits ≤ 128 and > 128 (two different reference decoder paths).
* Chunk that does not shrink (must be raw).

---

## 6. PXR24 (id 5)

Lossy for FLOAT (rounded to 24 bits), lossless for HALF and UINT.

### 6.1 Encoder

1. Build a byte buffer T. For each line y of the chunk (increasing y), for each channel in header order that has a
   line at y (and n_c > 0), with `w = width / xSampling` samples:
   * **UINT**: values `x_i` (u32). Differences `d_i = (x_i − x_{i−1}) mod 2^32` with `x_{−1} = 0` (the predictor restarts at
     every line of every channel). Append 4 byte planes of w bytes each: bits 31–24 of all d_i, then bits 23–16,
     then 15–8, then 7–0.
   * **HALF**: values `x_i` (u16 bit patterns). `d_i = (x_i − x_{i−1}) mod 2^16`, `x_{−1} = 0`. Append 2 planes: high bytes,
     then low bytes.
   * **FLOAT**: each value is first converted to a 24-bit pattern `f_i` (6.2). `d_i = (f_i − f_{i−1}) mod 2^24`, `f_{−1} = 0`.
     Append 3 planes: bits 23–16, then 15–8, then 7–0.
2. Compress T as one zlib stream (RFC 1950), like ZIP but **without** the section 2 transform.
3. Fallback to raw if the zlib output is ≥ S (see 1.5 for the reference's ">" quirk).

Size of T = Σ over present channel lines of `w × (4 for UINT, 2 for HALF, 3 for FLOAT)`.

### 6.2 FLOAT → 24-bit conversion (exact)

Let the float bits be split into sign `s` (bit 31), exponent field `e` (bits 30–23) and mantissa `m` (bits 22–0).
The result is a 24-bit value: bit 23 = s, bits 22–0 = a 23-bit magnitude field i:
* **Infinity** (e = 255, m = 0): `i = 0x7F8000`.
* **NaN** (e = 255, m ≠ 0): `i = 0x7F8000 | (m >> 8)`, and if `m >> 8 = 0` set i's lowest bit (so a NaN never becomes
  infinity). Payload bits below bit 8 are dropped.
* **Finite** (including zero and denormals): let `q = bits 30–0` of the float (that is, `e<<23 | m`).
  `i = (q + (q AND 0x80)) >> 8` — round half **up in magnitude** (ties away from zero) at bit 8; the carry may propagate
  into the exponent (correct rounding to the next binade).
  If that result is ≥ 0x7F8000 (rounding overflowed into infinity), use truncation instead: `i = q >> 8`.
* Result = `(s << 23) | i`.

Decoder: the 24-bit value v becomes float bits `v << 8` (the 8 low mantissa bits are 0).

Examples: 1.0 (0x3F800000) → 0x3F8000; 0x3F800080 → 0x3F8001 (tie rounds up); 0x3F80007F → 0x3F8000;
FLT_MAX (0x7F7FFFFF) → 0x7F7FFF (truncated, not infinity); −0.0 → 0x800000; 0x7FC00000 → 0x7FC000;
0x7F800001 (NaN with only low payload bits) → 0x7F8001; 0x00000080 (denormal) → 0x000001.

### 6.3 Decoder

1. Inflate the zlib stream. The inflated length must be at least what the line/channel walk consumes (a shorter
   result is an error; extra bytes are tolerated by the reference **[decoder robustness]**). Recommended: require
   exact length.
2. Walk lines and channels exactly as the encoder, reading the w-byte planes and summing differences modulo 2^32 /
   2^16 / 2^24 starting from 0 at each channel line. FLOAT values are emitted as `(sum << 8)` bits.

### 6.4 Edge cases

Infinity, NaN with payload only in the low 8 bits, values just below FLT_MAX, negative zero, denormals,
ties (bit pattern ending in 0x80), large deltas that wrap (for example 0xFFFFFFFF then 0 in UINT), HALF and UINT
round-trip exactly, width 1, sub-sampled channels.

---

## 7. B44 (id 6) and B44A (id 7)

Lossy for HALF channels (fixed rate: 32 bytes → 14 bytes per 4×4 block; B44A also 3 bytes for uniform blocks).
UINT and FLOAT channels are stored uncompressed inside the B44 payload. B44 and B44A share one decoder; B44A only
differs in that the encoder may emit 3-byte blocks.

### 7.1 Payload structure

For each channel in header order (planes as in PIZ 5.1: `nx = width / xSampling`, `ny = n_c`, rows = the channel's
lines in the chunk top to bottom; channels with nx × ny = 0 contribute nothing):
* **UINT / FLOAT channel:** its `nx × ny × 4` bytes, row by row, each sample as in U (LE). No transformation.
* **HALF channel:** the plane is cut into 4×4 blocks: block rows from top to bottom (starting rows 0, 4, 8, …), within a
  block row from left to right (starting columns 0, 4, …). Each block is encoded as either 14 or 3 bytes, written
  consecutively. Number of blocks = `ceil(nx/4) × ceil(ny/4)`.

Within a block, the 16 values are indexed `s[4r + c]` (r = row 0…3, c = column 0…3).

**Partial blocks (encoder):** when the plane's width or height is not a multiple of 4, missing columns repeat the
**last existing column** of that row and missing rows repeat the **last existing row**. **Decoder:** decode the full
block and write only the existing pixels.

### 7.2 Value ordering transform

Each half bit pattern s is mapped to a 16-bit key t that is monotonic in the represented value:
* if s is infinity or NaN (`s AND 0x7C00 = 0x7C00`): `t = 0x8000` (that is, the key of +0.0),
* else if the sign bit is set: `t = NOT s` (16-bit complement),
* else: `t = s OR 0x8000`.

Inverse (decoder) for a 16-bit t: if `t AND 0x8000`: `s = t AND 0x7FFF`; else `s = NOT t` (16 bits).

Consequences: infinities and NaNs are stored as +0.0; −0.0 maps to 0x7FFF and back to −0.0.

### 7.3 14-byte block encoding (encoder)

1. `K` = maximum of the 16 keys.
2. For `shift = 0, 1, 2, …`: compute for every i `d_i = R(K − t_i, shift)` where R rounds `x / 2^shift` to the
   nearest integer with **ties to even** (exactly: with `a = 2^shift − 1` and `b = floor(x / 2^shift) AND 1`,
   `R = floor((2x + a + b) / 2^(shift+1))`; for shift = 0, R = x).
   Then compute the 15 running differences, each biased by 32:

   | r index | value | | r index | value |
   |---|---|---|---|---|
   | r0 | d0 − d4 + 32 | | r8 | d5 − d6 + 32 |
   | r1 | d4 − d8 + 32 | | r9 | d9 − d10 + 32 |
   | r2 | d8 − d12 + 32 | | r10 | d13 − d14 + 32 |
   | r3 | d0 − d1 + 32 | | r11 | d2 − d3 + 32 |
   | r4 | d4 − d5 + 32 | | r12 | d6 − d7 + 32 |
   | r5 | d8 − d9 + 32 | | r13 | d10 − d11 + 32 |
   | r6 | d12 − d13 + 32 | | r14 | d14 − d15 + 32 |
   | r7 | d1 − d2 + 32 | | | |

   Stop at the first shift where all 15 r values are within 0 … 63. (This always happens by shift 11.)
3. **B44A flat block:** if the encoder is B44A and all 15 r values equal 32 (which, at the first valid shift, means all
   16 keys are equal), emit the 3-byte form (7.4) instead and stop.
4. Base value: for a channel **without** pLinear, `t0' = K − (d0 × 2^shift)` (so that the maximum pixel is
   reconstructed exactly); for a pLinear channel `t0' = t0` unchanged. (u16 arithmetic; cannot underflow.)
5. Output bytes: byte 0 = `t0' >> 8`, byte 1 = `t0' AND 0xFF` (**big-endian**), then 12 bytes holding a 96-bit
   **MSB-first** bit string made of the 6-bit fields `shift, r0, r1, r2, …, r14` in this order. (So byte 2 = shift << 2 |
   r0 >> 4, byte 3 = (r0 AND 15) << 4 | r1 >> 2, byte 4 = (r1 AND 3) << 6 | r2, byte 5 = r3 << 2 | r4 >> 4, …,
   byte 13 = (r13 AND 3) << 6 | r14.)

Since shift ≤ 11 here, byte 2 of a 14-byte block is always < 48.

### 7.4 3-byte block (flat field)

Bytes: `t0 >> 8`, `t0 AND 0xFF` (the common key, big-endian), then **0xFC**.

### 7.5 Decoder

For each HALF block: need 3 bytes; if **byte 2 ≥ 52** (that is, the 6-bit shift field would be ≥ 13), it is a 3-byte
block: all 16 keys = byte0 << 8 | byte1. Otherwise it is a 14-byte block (need 14 bytes):
* `t0 = byte0 << 8 | byte1`, `shift = byte2 >> 2`, r0…r14 = the next 15 six-bit fields (MSB-first, as in 7.3).
* With `δ(r) = (r × 2^shift − 32 × 2^shift) mod 65536`, all additions modulo 65536:
  * `t4 = t0 + δ(r0)`, `t8 = t4 + δ(r1)`, `t12 = t8 + δ(r2)`
  * `t1 = t0 + δ(r3)`, `t5 = t4 + δ(r4)`, `t9 = t8 + δ(r5)`, `t13 = t12 + δ(r6)`
  * `t2 = t1 + δ(r7)`, `t6 = t5 + δ(r8)`, `t10 = t9 + δ(r9)`, `t14 = t13 + δ(r10)`
  * `t3 = t2 + δ(r11)`, `t7 = t6 + δ(r12)`, `t11 = t10 + δ(r13)`, `t15 = t14 + δ(r14)`
* Map every t back to a half bit pattern (7.2 inverse), then apply the pLinear conversion (7.6) if the channel has it.

Errors: running out of payload bytes for a block or for a UINT/FLOAT plane. After all channels, re-interleave the
planes into U (inverse of 7.1 / 5.1). Values decoded from a corrupt block can be any half including Inf/NaN.

### 7.6 pLinear channels

A channel's `pLinear` flag (from the channel list) marks perceptually linear (for example logarithmic) data. B44
converts those values to a linear-light-like representation before packing and back after unpacking, through two
65536-entry tables indexed by the half bit pattern:

* **Before packing (encoder), table E:**
  * x is Inf or NaN → 0x0000;
  * `0x558C ≤ x < 0x8000` (positive values ≥ 88.75, above 8 × ln(HALF_MAX) ≈ 88.72) → 0x7BFF (HALF_MAX);
  * otherwise → `half(exp(float(x) / 8))`.
* **After unpacking (decoder), table L:**
  * x is Inf or NaN → 0x0000;
  * x > 0x8000 (negative, excluding −0.0) → 0x0000;
  * otherwise (including +0.0 and −0.0) → `half(8 × ln(float(x)))`; for ±0 this is −Inf (0xFC00).
* The computation is in binary32: `float(x)` is exact, the division by 8 and the multiply by 8 are binary32 operations,
  exp/ln are the binary32 library functions, and `half()` rounds to nearest, ties to even, with overflow to ±Inf and
  binary16 denormals.
* Because results depend on binary32 exp/ln, precompute the tables. Reference hashes of the tables written as 65536
  little-endian u16 values (131072 bytes), computed with both macOS libm `expf`/`logf` and double-precision
  `exp`/`log` rounded to binary32 (both agree): E: SHA-256 `aeb0250f258b38cb0fb4fb86dca1e32f1f512a93b3ceca1fcf825a523324aea1`,
  L: SHA-256 `92de2787a44544c6e4d54f942f38cf50453b342f179189dbaeb514743fc9109f`.
  Sample entries: E[0x0000] = 0x3C00, E[0x3C00] = 0x3C88, E[0xBC00] = 0x3B0F, E[0x4000] = 0x3D23, E[0x558B] = 0x7BF7,
  E[0x558C] = 0x7BFF, E[0x7C00] = 0x0000, E[0x8000] = 0x3C00, E[0xFBFF] = 0x0000; L[0x0000] = 0xFC00, L[0x0001] = 0xD829,
  L[0x3C00] = 0x0000, L[0x4000] = 0x458C, L[0x4170] = 0x4800, L[0x7BFF] = 0x558C, L[0x8000] = 0xFC00, L[0x8001] = 0x0000.
* Encoder: apply E to all 16 values of the block, then 7.2/7.3 with **the step-4 base adjustment disabled** (t0' = t0). Decoder: unpack,
  then apply L to all 16 values (also for 3-byte blocks).

### 7.7 Fallback

Write raw when the payload is ≥ S (the reference Core uses > S, see 1.5). Small planes usually expand (for example
1 × 1 HALF: 2 bytes raw vs 14).

### 7.8 Test vectors and edge cases

* Block of 16 × 0x3C00 (1.0), not pLinear. Keys all 0xBC00.
  * B44A: `BC 00 FC`.
  * B44: `BC 00 02 08 20 82 08 20 82 08 20 82 08 20` (shift 0, all r = 32).
* Blocks containing Inf/NaN (decode as +0.0); a block of +0.0 and +Inf only is "flat" under B44A.
* Mixed signs (keys straddling 0x8000), −0.0, denormals, maximum spread (−HALF_MAX and +HALF_MAX in one block → shift 11).
* Planes 1×1, 1×4, 4×1, 3×3, 5×5, 7×2 (the size-equality case), widths/heights ≡ 1, 2, 3 mod 4.
* pLinear channel with values above 88.75, negative values, zero.
* Mixed HALF/FLOAT/UINT channels in a chunk, sub-sampled HALF channels (blocks are formed on the sub-sampled plane).
* Decoder: a 14-byte block whose shift field is 12 (byte 2 in 48…51) is valid input; byte 2 ≥ 52 is always the
  3-byte form, even in a file declared B44 (non-A).

---

## 8. DWAA (id 8) and DWAB (id 9)

DWAA and DWAB share one byte format and one decoder. They differ only in lines per chunk (32 vs 256) and in an encoder
choice (8.14). DWA is lossy for the channels it classifies as LOSSY_DCT (normally R, G, B, Y, RY, BY of type HALF or
FLOAT) and lossless for every other channel.

**Reference build.** The decoder behaviour below is the **scalar** code path of OpenEXR 3.5.2 as built by the
reference script: on x64 the SSE2 code is compiled out and the AVX/F16C run-time selection is disabled, and
everything is compiled with `-ffp-contract=off` (no fused multiply-add on any platform). One exception remains on
arm64: the conversions half → binary32 (8.10) and binary32 → half (8.11) use the processor's conversion
instructions there, and the portable bit manipulation on x64. Both are exact IEEE conversions (nearest, ties to even,
denormals kept), so they give identical bits for every non-NaN value; NaN payloads may differ (8.11, 12).

All floating-point arithmetic below is **binary32** with round-to-nearest-even after every operation, evaluated in
the order written (left to right for chains of additions; `a·x + b·y` means: round `a·x`, round `b·y`, round the
sum). There is no binary64 arithmetic anywhere in the DWA decoder.

### 8.1 Chunk payload layout

| part | content |
|---|---|
| header | 11 × u64 LE (8.2) |
| rule block | only when version ≥ 2 (8.3) |
| UNKNOWN section | `unknownCompressedSize` bytes (8.6) |
| AC section | `acCompressedSize` bytes (8.8) |
| DC section | `dcCompressedSize` bytes (8.9) |
| RLE section | `rleCompressedSize` bytes (8.7) |

The four sections are consecutive, in this order, immediately after the rule block. A section of size 0 is absent.
**[decoder robustness]** The reference tolerates bytes after the RLE section; recommended: tolerate them too.

### 8.2 Header (11 × u64 LE)

| index | name | meaning |
|---|---|---|
| 0 | version | 0, 1 or 2. Writers emit 2. > 2 is an error. |
| 1 | unknownUncompressedSize | byte size of the UNKNOWN plane buffer (8.6) |
| 2 | unknownCompressedSize | byte size of the UNKNOWN section |
| 3 | acCompressedSize | byte size of the AC section |
| 4 | dcCompressedSize | byte size of the DC section |
| 5 | rleCompressedSize | byte size of the RLE section |
| 6 | rleUncompressedSize | byte size of the run-length-coded RLE data (before zlib) |
| 7 | rleRawSize | byte size of the RLE plane buffer (8.7) |
| 8 | acCount | number of u16 AC symbols (8.8) |
| 9 | dcCount | number of u16 DC values (8.9) |
| 10 | acCompression | 0 = static Huffman, 1 = deflate (zlib) |

Versions: 0 = original; 1 = adds the end-of-block AC symbol (8.8; the decoder is the same for all versions);
2 = adds the rule block. Versions 0 and 1 use the legacy rules (8.3).

Decoder checks (all **[decoder robustness]**, the reference rejects the chunk): P < 88; any field with the top bit set
(as i64 < 0); `88 + unknownCompressedSize + acCompressedSize + dcCompressedSize + rleCompressedSize > P` (also check each
term against P to avoid overflow); `unknownUncompressedSize > S`, `rleRawSize > S` or their sum `> S`; the sections
extending past P once the rule block size is added; version > 2; `acCompression ∉ {0, 1}` when `acCompressedSize > 0`.
Size consistency (checked after classification, 8.4): let `need_UNKNOWN` = Σ over UNKNOWN channels of their plane size,
`need_RLE` likewise for RLE channels (plane size = `width/xSampling × n_c × bytesPerSample`). If `need_X > 0` the
uncompressed size field must be ≥ `need_X` and the compressed size must be non-zero; if `need_X = 0`, the uncompressed
and compressed size (and, for RLE, `rleUncompressedSize`) must all be 0. `dcCount > 0` requires `dcCompressedSize > 0`.

### 8.3 Channel classification rules

**Rule block (version ≥ 2).** A u16 LE `ruleSize` = total byte size of the block **including** these 2 bytes
(≥ 2; the reference also requires more than 2 bytes to remain in the payload when it starts reading it), followed by
`ruleSize − 2` bytes of rules, back to back, until exactly that many bytes are consumed. Each rule:

| bytes | content |
|---|---|
| 1…129 | channel-name suffix, NUL-terminated: at most 128 non-NUL bytes then a 0 byte |
| 1 | packed byte: bits 7–4 = `cscIndex + 1` (0 = none, 1 = R, 2 = G, 3 = B), bits 3–2 = scheme (0 = UNKNOWN, 1 = LOSSY_DCT, 2 = RLE), bit 1 unused (write 0), bit 0 = case-insensitive |
| 1 | pixel type (0 = UINT, 1 = HALF, 2 = FLOAT) |

Errors: a suffix of 129 non-NUL bytes; a rule that does not fit in the remaining block bytes (the name, its NUL and
the 2 bytes); `cscIndex + 1 > 3`; scheme 3; pixel type > 2. An empty rule list (`ruleSize = 2`) is valid (every channel
is then UNKNOWN).

**Default rules written by the reference (version 2)**, in this order, all case-sensitive:

| suffix | scheme | type | csc |
|---|---|---|---|
| R | LOSSY_DCT | HALF, then FLOAT | 0 (R) |
| G | LOSSY_DCT | HALF, then FLOAT | 1 (G) |
| B | LOSSY_DCT | HALF, then FLOAT | 2 (B) |
| Y | LOSSY_DCT | HALF, then FLOAT | none |
| BY | LOSSY_DCT | HALF, then FLOAT | none |
| RY | LOSSY_DCT | HALF, then FLOAT | none |
| A | RLE | UINT, then HALF, then FLOAT | none |

That is 15 rules (each (suffix, type) pair is a separate rule). The encoder writes only the **relevant** rules: walking
this table in order, a rule is written when at least one channel of the part matches it (8.4), each rule at most once.
Encoder-side, the rules are a choice **[encoder choice]**: any rule set may be written, the decoder only uses the rules
in the file.

**Legacy rules (versions 0 and 1)**, all **case-insensitive**, in this order, each for HALF then FLOAT unless noted:
`r`, `red` (csc R); `g`, `grn`, `green` (csc G); `b`, `blu`, `blue` (csc B); `y`, `by`, `ry` (no csc) → LOSSY_DCT;
`a` → RLE for UINT, HALF and FLOAT.

Test vector: R, G, B HALF channels → rule block `0E 00 | 52 00 14 01 | 47 00 24 01 | 42 00 34 01`.
A HALF alone → `06 00 41 00 08 01`. Y HALF alone → `06 00 59 00 04 01`.

### 8.4 Classifying the channels and forming the colour (CSC) sets

**Suffix and prefix.** The suffix of a channel name is the part after the **last** `.` (the whole name if there is
none); the prefix is everything before the suffix, including that last `.` (empty if none).

**Matching.** A rule matches a channel when the pixel types are equal and the suffix equals the rule's suffix (byte
comparison, or ASCII case-insensitive comparison when the rule's bit 0 is set). Every channel starts as UNKNOWN; all
rules are tried in rule order, and **every** matching rule applies: the channel's scheme becomes that rule's scheme
(so the last matching rule wins), and when the rule has a csc index i, the channel becomes the candidate for colour
component i of its prefix group (again the last match wins; with the default rules at most one rule matches a channel).
UINT channels never match a LOSSY_DCT default rule; **[encoder choice]** never write rules that make a UINT channel
LOSSY_DCT (the reference would decode it as HALF data into a 4-byte channel; **[decoder robustness]** reject).

**Prefix groups.** Groups are created in channel order (header order): each channel looks up the group whose prefix
is byte-identical (case-sensitive, same length) to its own, creating one if none exists. Each group has three slots
(R, G, B) filled as above.

**CSC sets.** Walking the groups in creation order, a group forms a CSC set when all three slots are filled and the
three channels have the same xSampling and the same ySampling (pixel types may differ: HALF and FLOAT can be mixed in
one set). The three channels of a set are coded together (8.5) in the order R, G, B. **[decoder robustness]** If a
member of a set is not LOSSY_DCT (possible with unusual rules) the reference rejects the chunk.

A LOSSY_DCT channel not in a CSC set (for example R and G without B, or Y) is coded alone. A channel classified
LOSSY_DCT whose plane is empty in this chunk (width or n_c = 0) contributes nothing.

### 8.5 Coding order of the LOSSY_DCT data

The AC symbols and DC values of all LOSSY_DCT channels go into two global arrays (8.8, 8.9). They are produced and
consumed in this order:
1. every CSC set, in set order (8.4), as one 3-component unit;
2. then every remaining LOSSY_DCT channel, in header order, as a 1-component unit.

For a unit, let `w = width / xSampling` and `h = n_c` of its channel(s) (same for the three channels of a set),
`bx = ceil(w/8)`, `by = ceil(h/8)`. The plane is cut into 8×8 blocks, block rows top to bottom, blocks left to right.

* **DC:** the unit writes `ncomp × bx × by` consecutive u16 values: component 0's DC values for all blocks in block
  order, then component 1's, then component 2's.
* **AC:** for each block in block order, for each component 0…ncomp−1, that component's AC symbols for the block
  (8.8). AC symbols of the components are therefore interleaved per block.

The decoder walks the same order, taking the units' DC values and AC symbols from the front of the global arrays.
**[decoder robustness]** The reference checks that at least `ncomp × bx × by` DC values remain before each unit and
that no AC symbol is read past `acCount`; it does not check that every value was consumed. Recommended: require that
too, or tolerate leftovers like the reference.

The components of a CSC unit are Y′, Cb, Cr (8.12). For a 1-component unit, the component is the channel itself.

### 8.6 UNKNOWN channels

Plane buffer: the concatenation, over the UNKNOWN channels in header order, of each channel's rows (its n_c lines in
the chunk, top to bottom), each row its `w` samples exactly as in U (1.1, little-endian). Size =
`unknownUncompressedSize` (= Σ plane sizes). Section: one zlib stream (RFC 1950) of that buffer, **without** the section 2
transform. The inflated size must be exactly `unknownUncompressedSize` and the zlib stream must consume exactly
`unknownCompressedSize` bytes. Reference compression level **[encoder choice]**: 9.

### 8.7 RLE channels

Plane buffer: the concatenation, over the RLE channels in header order, of each channel's **byte planes**: for a channel
with `b` bytes per sample and `m = w × n_c` samples (taken row by row as in U), plane k (k = 0…b−1) is byte k of every
sample, in sample order (byte 0 = the first byte in U, that is the least significant byte). So a HALF channel
contributes m low bytes then m high bytes. Size = `rleRawSize`.

Section: the plane buffer is run-length coded with the byte format of 3.1 (**no** section 2 transform), giving
`rleUncompressedSize` bytes, which are then compressed as one zlib stream of `rleCompressedSize` bytes. Decoder: inflate
to exactly `rleUncompressedSize` bytes, run-length decode to exactly `rleRawSize` bytes (3.2 errors apply), then
re-interleave the byte planes into the rows. Reference encoder **[encoder choice]**: the run-length coder of 3.3 and zlib
level 9.

### 8.8 AC symbols and the AC section

**Coefficients.** Each 8×8 block of a component is stored as 64 binary16 coefficients in **zig-zag order** z = 0…63
(z = 0 is the DC coefficient). The natural position of a coefficient is `n = 8·v + u` (v = vertical frequency = row of
the block, u = horizontal frequency = column). The zig-zag position of each natural position (row by row, n = 0…63):

```
 0  1  5  6 14 15 27 28
 2  4  7 13 16 26 29 42
 3  8 12 17 25 30 41 43
 9 11 18 24 31 40 44 53
10 19 23 32 39 45 52 54
20 22 33 38 46 51 55 60
21 34 37 47 50 56 59 61
35 36 48 49 57 58 62 63
```

(the usual JPEG zig-zag: the entry at row v, column u is z.)

**AC run-length symbols** (u16 each, for z = 1…63 of one block):
* A symbol whose **high byte is 0xFF** is a run: low byte `k`. If k ≥ 1, the next k coefficients are +0 (0x0000). If
  k = 0 (symbol 0xFF00, versions ≥ 1), all remaining coefficients of the block are +0 (end of block).
* Any other symbol is a literal coefficient (a binary16 bit pattern) for the current position.

Decoder: start at z = 1 with all 63 AC coefficients +0; read symbols until z ≥ 64; a run advances z by k (by 64 for
k = 0); a literal is stored at z and z advances by 1. A run that jumps past 63 simply ends the block (no error).
Running out of symbols before z ≥ 64 is an error. Let **lnz** = the zig-zag position of the **last literal** stored in
the block (0 if none) — counted even when the literal's value is 0x0000 or 0x8000. lnz decides the inverse-DCT path
(8.10).

Consequence: binary16 values 0xFF00…0xFFFF (negative NaNs) can never be AC literals.

Reference encoder **[encoder choice]**: walk z = 1…63. A coefficient ≠ 0x0000 (note: −0.0 = 0x8000 is not zero here) is
a literal. At a 0x0000, measure the run of 0x0000 coefficients: length 1 → a literal 0x0000; if the run reaches z = 63
inclusive → 0xFF00 (end of block); otherwise 0xFF00 | length (2…62). Then continue after the run. Example: AC
coefficients (z = 1…63) `3C00, 0, 0, 0, 3800, 0 …` → symbols `3C00 FF03 3800 FF00`; a block with all AC = 0 → `FF00`;
`0, 3C00, 0 …` → `0000 3C00 FF00`. **[encoder choice]** never let a coefficient with high byte 0xFF reach the AC stream
(the reference can produce one only from a NaN coefficient; replace it, for example by 0x7E00 or by 0).

**AC section.** The concatenation of all AC symbols of the chunk (8.5) is an array of `acCount` u16 values, compressed
according to `acCompression`:
* **0, static Huffman:** exactly the Huffman stream of 5.4 (20-byte header, packed code-length table, bits), with
  N = acCount symbols, the same run symbol mechanism and the same decoder requirements. The stream occupies exactly
  `acCompressedSize` bytes.
* **1, deflate:** one zlib stream of the symbols written as u16 **LE** (2 × acCount bytes); the inflated size must be
  exactly `2 × acCount`. No section 2 transform.

If `acCount = 0` the section is absent (`acCompressedSize = 0`). **[decoder robustness]** If `acCompressedSize = 0` but
`acCount > 0`, the reference decodes the units from an all-zero symbol buffer (symbol 0x0000 is a literal +0, so
every block reads 63 literal zeros and lnz = 63); recommended: reject.

### 8.9 DC values and the DC section

The DC coefficients (z = 0, binary16) of all units, ordered as in 8.5, form an array of `dcCount` u16 values. Section:
the values written as u16 **LE** (2 × dcCount bytes), transformed by the **section 2** byte split and delta predictor,
then compressed as one zlib stream of `dcCompressedSize` bytes. Decoder: inflate to exactly `2 × dcCount` bytes,
inverse section 2, read the u16 LE values. Reference compression level **[encoder choice]**: the part's zip level
(the same default as ZIP, section 4).

### 8.10 Decoding one 8×8 block (scalar path, exact)

Constants (binary32; hexadecimal bit patterns are normative, decimal values are for reading):

| name | bits | ≈ value | definition |
|---|---|---|---|
| a | 0x3EB504FB | 0.353553623 | 0.5 · cos(π′/4) |
| b | 0x3EFB14BF | 0.490392655 | 0.5 · cos(π′/16) |
| c | 0x3EEC8361 | 0.461939842 | 0.5 · cos(π′/8) |
| d | 0x3ED4DB36 | 0.415734947 | 0.5 · cos(3π′/16) |
| e | 0x3E8E39E5 | 0.27778545 | 0.5 · cos(5π′/16) |
| f | 0x3E43EF33 | 0.19134216 | 0.5 · cos(3π′/8) |
| g | 0x3DC7C60B | 0.0975457057 | 0.5 · cos(7π′/16) |
| k | 0x3EB504FA | 0.353553593 | the literal 3.535536e-01 rounded to binary32 (**not** equal to a) |

π′ is the binary32 value of 3.14159 (0x40490FD0), **not** π. Each argument is computed in binary32 (`π′ / 4`,
`3 × π′` rounded then `/ 16`, and so on), its cosine is the correctly rounded binary32 cosine, and the product by 0.5
is exact. These values were checked against both the platform `cosf` and binary64 `cos` rounded to binary32, and against
compile-time constant folding (identical). Use the bit patterns.

**Step 1 — dezigzag.** Every coefficient is converted from binary16 to binary32 (exact) and placed at its natural
position: `F[n] = float(coef[zigzag(n)])`, n = 0…63 (natural layout, `F[8v+u]`).

**Step 2a — DC-only block (lnz = 0).** All 64 outputs are the same value `t = (float(dc) × k) × k` (two binary32
multiplications, in that order). This shortcut is taken whenever lnz = 0, and it does **not** give the same bits as
step 2b would (2b gives `a × (a × dc)` with a different constant), so it must be reproduced. (For a single component the
two paths happen to give the same binary16 result for every finite DC value, but they can differ in binary32, which
matters before the colour transform of a CSC unit.)

**Step 2b — full inverse DCT (lnz ≥ 1).** In place on F, first all rows, then all columns.

Row pass, for each row r = 0…7, with `y0…y7 = F[8r + 0…7]`:
* `α0 = c·y2`, `α1 = f·y2`, `α2 = c·y6`, `α3 = f·y6`
* `β0 = ((b·y1 + d·y3) + e·y5) + g·y7`
* `β1 = ((d·y1 − g·y3) − b·y5) − e·y7`
* `β2 = ((e·y1 − b·y3) + g·y5) + d·y7`
* `β3 = ((g·y1 − e·y3) + d·y5) − b·y7`
* `θ0 = a·(y0 + y4)`, `θ3 = a·(y0 − y4)` (the sum/difference is rounded first), `θ1 = α0 + α3`, `θ2 = α1 − α2`
* `γ0 = θ0 + θ1`, `γ1 = θ3 + θ2`, `γ2 = θ3 − θ2`, `γ3 = θ0 − θ1`
* outputs: `F[8r+0] = γ0 + β0`, `F[8r+1] = γ1 + β1`, `F[8r+2] = γ2 + β2`, `F[8r+3] = γ3 + β3`,
  `F[8r+4] = γ3 − β3`, `F[8r+5] = γ2 − β2`, `F[8r+6] = γ1 − β1`, `F[8r+7] = γ0 − β0`.

Column pass: the same formulas for each column u = 0…7 with `y_i = F[8i + u]` (values after the row pass), results
written to `F[8i + u]` in the same pattern (output i = 0…7 as above).

Every product of a constant and a value is rounded to binary32 before it is added. No fused multiply-add.

The reference skips the row pass for trailing rows known to be all zero (row r is skipped when lnz is smaller than the
zig-zag index of `F[8r]`: rows 1…7 are skipped when lnz < 2, rows 2…7 when lnz < 3, 3…7 when < 9, 4…7 when < 10, 5…7 when
< 20, 6…7 when < 21, row 7 when < 35). Such rows contain only +0.0 and the row pass maps a row of +0.0 to +0.0, so this
optimisation does not change any bit; an implementation may run the full row pass.

The spatial value of pixel (x, y) of the block is `F[8y + x]` (y = row, x = column).

**Step 3 — colour transform** of a CSC unit (8.12), per pixel, on the three components' binary32 values.

**Step 4 — to binary16.** Every value is rounded to binary16 (8.11).

**Step 5 — to linear.** For a CSC unit, and for a 1-component unit whose channel does **not** have the pLinear flag, each
binary16 pattern h is replaced by `toLinear[h]` (8.13). A 1-component unit of a pLinear channel skips this step. (The
CSC path always applies the curve, whatever the channels' pLinear flags.)

**Step 6 — write.** Only the pixels inside the plane are written: for the last block column, `w − 8(bx − 1)` columns; for
the last block row, `h − 8(by − 1)` rows. HALF channels receive the binary16 pattern (u16 LE). FLOAT channels receive
`float(h)` (exact widening of the final binary16, u32 LE): a FLOAT channel coded by DWA therefore always decodes to
values representable in binary16.

### 8.11 binary32 → binary16 rounding

Round to nearest, ties to even, with binary16 denormals; magnitudes ≥ 65520 (bit pattern of the magnitude
> 0x477FEFFF) become ±Inf; magnitudes ≤ 2^−25 become ±0 (2^−25 itself ties to 0). Sign kept (−0.0 → 0x8000). NaN: on x64
(scalar code) the result is sign | 0x7C00 | (top 10 mantissa bits), with the lowest bit set if those are all 0 (payload
kept, signalling NaNs stay signalling); on arm64 (hardware conversion) the quiet bit 0x0200 is also set. NaN values only
arise from Inf/NaN coefficients or overflowing arithmetic; their binary32 payload and sign after the arithmetic is also
processor dependent (x86 produces the "default NaN" with the sign bit set, arm64 without). For channels that go through
`toLinear`, every NaN and Inf maps to 0x0000, so the difference is invisible; it is only visible in the 1-component
units of pLinear channels. Recommended: reproduce the x64 behaviour, or
leave it to the platform's natural arithmetic, and keep NaN out of conformance tests.

### 8.12 Colour transform (Rec. 709 weights, chroma zero at black)

Decoder (inverse), per pixel, with `Y = comp0`, `Cb = comp1`, `Cr = comp2` (binary32):
* `R = Y + 1.5747·Cr`
* `G = (Y − 0.1873·Cb) − 0.4682·Cr`
* `B = Y + 1.8556·Cb`

Encoder (forward), from the nonlinear R′, G′, B′:
* `Y = (0.2126·R + 0.7152·G) + 0.0722·B`
* `Cb = ((−0.1146)·R − 0.3854·G) + 0.5·B`
* `Cr = (0.5·R − 0.4542·G) − 0.0458·B`

Constants are binary32: 1.5747 = 0x3FC98FC5, 0.1873 = 0x3E3FCB92, 0.4682 = 0x3EEFB7E9, 1.8556 = 0x3FED844D,
0.2126 = 0x3E59B3D0, 0.7152 = 0x3F371759, 0.0722 = 0x3D93DD98, 0.1146 = 0x3DEAB368 (used negated), 0.3854 = 0x3EC55326,
0.4542 = 0x3EE88CE7, 0.0458 = 0x3D3B98C8. Products rounded, then sums left to right, as written.

The transform is applied after the inverse DCT of the three components of a block and before the binary16 rounding
(8.10 step 3). When all three components of a block are DC-only, applying it to one value and replicating is
equivalent (all 64 values are equal).

### 8.13 Perceptual curve tables

Two 65536-entry tables map a binary16 pattern x to a binary16 pattern. They are computed in binary32 (`float(x)` is
exact; every operation rounds to binary32) and the result is rounded to binary16 with 8.11 (scalar rounding on every
platform):

**toNonlinear (encoder):**
* x = 0x0000, or x is Inf/NaN (`x AND 0x7C00 = 0x7C00`) → 0x0000;
* otherwise `s = −1` if float(x) < 0 else `+1` (so −0.0 gives +1), `f = |float(x)|`;
  if `f ≤ 1`: `z = powf(f, 1/2.2)` where the exponent is the binary32 quotient `1.0 / 2.2` (0x3EE8BA2E);
  else `z = logf(f) / 2.2 + 1.0` (2.2 = 0x400CCCCD; division then addition, binary32);
  result = half(s × z). (So 0x8000 → 0x0000.)

**toLinear (decoder):**
* x = 0x0000, or x is Inf/NaN → 0x0000;
* otherwise s and f as above; if `f ≤ 1`: `z = powf(f, 2.2)`; else `z = powf(9.02501329156, f − 1.0)` with the base
  rounded to binary32 (0x41106674 ≈ 9.02501297) and `f − 1.0` in binary32; result = half(s × z).
  (0x8000 → 0x0000; tiny negative values give 0x8000; large values overflow to ±Inf: the first +Inf entry is 0x460B.)

`powf` and `logf` are the binary32 library functions; the tables below match both macOS libm and the correctly
rounded results (binary64 `pow`/`log` rounded to binary32), which agree for every entry. Ship the tables as data and
verify them. SHA-256 of each table written as 65536 little-endian u16 (131072 bytes):
* toLinear: `72e80880a49b9428073cf87e2b181a6da74cfac9f8f7f4c6e459f0ac1f0225a5`
* toNonlinear: `9439b1fe1d69d77b3ea98dbe0af73e5bb7f4224b61eec3335a40b0c31bbccab8`

Sample entries (x → toLinear, toNonlinear): 0000 → 0000, 0000; 0001 → 0000, 1043; 03FF → 0000, 2237; 3800 → 32F7, 39D7;
3C00 → 3C00, 3C00; 3C01 → 3C02, 3C00; 4000 → 4883, 3D43; 4400 → 61BE, 3E85; 4900 → 7C00, 4018; 7BFF → 7C00, 460A;
7C00 → 0000, 0000; 8000 → 0000, 0000; 8001 → 8000, 9043; BC00 → BC00, BC00; C000 → C883, BD43; FBFF → FC00, C60A.

### 8.14 Encoder

Everything in this subsection is **[encoder choice]** except the data layout it produces; the reference behaviour is
given so that files resemble OpenEXR's.

1. **Classification:** write the relevant default rules (8.3), version 2.
2. **FLOAT LOSSY_DCT channels** are first converted to binary16: each value is clamped to [−65504, 65504] (NaN passes
   through unchanged) and rounded with 8.11.
3. **Blocks:** for each unit and block (8.5), each component's 8×8 input is read from its binary16 plane with **mirrored
   edges**: a column index `vx ≥ w` becomes `2w − 1 − vx`, and if that is negative, `w − 1`; rows likewise with h. (So
   the samples beyond the edge mirror the last ones: w → w−1, w+1 → w−2, …)
4. **Nonlinear:** each sample h becomes `toNonlinear[h]` (for a CSC unit always; for a 1-component unit unless the
   channel is pLinear), then binary32.
5. **Forward colour transform** for a CSC unit (8.12).
6. **Forward DCT** of each component (in place; first rows, then columns). Constants `cK = cosf(π′·K/16)` for K = 1…7
   with the argument computed as `(π′ × K) / 16` in binary32: c1 = 0x3F7B14BF, c2 = 0x3F6C8361, c3 = 0x3F54DB36,
   c4 = 0x3F3504FB, c5 = 0x3F0E39E5, c6 = 0x3EC3EF33, c7 = 0x3E47C60B, and `cKh = 0.5·cK` (c1h = 0x3EFB14BF,
   c2h = 0x3EEC8361, c3h = 0x3ED4DB36, c5h = 0x3E8E39E5, c6h = 0x3E43EF33, c7h = 0x3DC7C60B).
   Row pass, for `s0…s7` = a row: `A0 = s0+s7`, `A1 = s1+s2`, `A2 = s1−s2`, `A3 = s3+s4`, `A4 = s3−s4`, `A5 = s5+s6`,
   `A6 = s5−s6`, `A7 = s0−s7`; `K0 = c4·(A0+A3)`, `K1 = c4·(A1+A5)`; `out0 = 0.5·(K0+K1)`, `out4 = 0.5·(K0−K1)`;
   `x = A2−A6`, `y = A0−A3`: `out2 = c6h·x + c2h·y`, `out6 = c6h·y − c2h·x`; `K0 = c4·(A1−A5)`, `K1 = (−c4)·(A2+A6)`;
   `x = A7−K0`, `y = A4+K1`: `out3 = c3h·x − c5h·y`, `out5 = c5h·x + c3h·y`; `x = A7+K0`, `y = K1−A4`:
   `out1 = c1h·x − c7h·y`, `out7 = c7h·x + c1h·y`.
   Column pass: the same with the column values, except that the rotations use the full constants and halve the
   result: `out2 = 0.5·(c6·x + c2·y)`, `out6 = 0.5·(c6·y − c2·x)`, `out3 = 0.5·(c3·x − c5·y)`,
   `out5 = 0.5·(c5·x + c3·y)`, `out1 = 0.5·(c1·x − c7·y)`, `out7 = 0.5·(c7·x + c1·y)` (out0 and out4 as in the row pass).
   With this scaling a constant block of value v has DC ≈ 8v and the decoder's inverse (8.10) reconstructs v.
7. **Quantisation.** Tolerances: `q = dwaCompressionLevel / 100000` (binary32; negative → 0). The level comes from the
   writer's settings (OpenEXR: per-part setting, default **45**; the C++ library also picks it up from a float header
   attribute named `dwaCompressionLevel` when one is inserted; the decoder never needs it). Two tolerance tables, in
   natural order: `tolY[n] = (q × QY[n]) / 10` and `tolC[n] = (q × QC[n]) / 17` (binary32, multiply then divide), with
   the JPEG tables (natural order, row by row):

   QY: `16 11 10 16 24 40 51 61 / 12 12 14 19 26 58 60 55 / 14 13 16 24 40 57 69 56 / 14 17 22 29 51 87 80 62 /
   18 22 37 56 68 109 103 77 / 24 35 55 64 81 104 113 92 / 49 64 78 87 103 121 120 101 / 72 92 95 98 112 100 103 99`

   QC: `17 18 24 47 99 99 99 99 / 18 21 26 66 99 99 99 99 / 24 26 56 99 99 99 99 99 / 47 66 99 99 99 99 99 99 /`
   then 32 × `99`.

   Component 0 of a unit (Y′ of a CSC unit, or the single channel) uses tolY; components 1 and 2 (Cb, Cr) use tolC.
   For each coefficient, `v = half(F[n])` (8.11) and the tolerance t = tol[n]: if v is Inf/NaN it is kept; if
   `|float(v)| < t` it becomes **+0 (0x0000)**; otherwise it is replaced by a binary16 value of the same sign whose
   magnitude m satisfies `|float(m) − |float(v)|| < t` and has as few set bits as the reference's search finds (ties
   prefer the smaller difference), or v itself. The reference's search tries a handful of candidates obtained by
   masking/rounding the low mantissa bits of v at the scale of t; it is a heuristic, not an exhaustive minimum, and does
   not need to be reproduced: any value within the tolerance is a valid encoding, and the decoder has no
   dequantisation step. A simple valid strategy: among the candidates formed by clearing the low j bits of the
   magnitude, or adding 2^j then clearing the low j bits (j = 0…14), keep those within the tolerance and pick the one
   with the fewest set bits, ties by smallest difference.
8. Store the DC (z = 0) and RLE-code the AC (8.8). Never emit a literal with high byte 0xFF.
9. **AC compression:** the reference uses static Huffman, except DWAA in tiled (and deep tiled) parts, which uses deflate.
   DWAB always uses static Huffman. Compression levels: 9 for the UNKNOWN, deflate-AC and RLE zlib streams, the part's zip
   level for DC.
10. **Fallback:** write U raw when the total payload is ≥ S (1.5).

Reference quirk (informative): if the reference's Huffman encoder fails with an out-of-range error, it copies the
uncompressed buffer using its *allocated* size instead of S.

### 8.15 DWAA versus DWAB

| | DWAA | DWAB |
|---|---|---|
| lines per scan-line chunk | 32 | 256 |
| AC compression written by the reference | static Huffman for scan lines, deflate for tiles | static Huffman |
| byte format, decoder | identical | identical |

Both are valid for tiled parts (one tile per chunk; the 8×8 block grid starts at the tile's top-left corner). Neither
is valid for deep parts.

### 8.16 Decoder summary

1. If P = S: raw (1.5). Otherwise parse the header (8.2) and, for version ≥ 2, the rule block; else use the legacy
   rules.
2. Classify the channels and form the CSC sets (8.4). Check the sizes (8.2).
3. Inflate the UNKNOWN section, decode the AC section (Huffman or zlib), inflate and un-transform the DC section,
   inflate and run-length decode the RLE section.
4. Decode the CSC units, then the single LOSSY_DCT channels (8.5, 8.10 to 8.13).
5. Copy the RLE channels (re-interleaving byte planes) and the UNKNOWN channels (rows) into U.
6. Bytes of U not written by any of the above stay 0 (the reference clears U first).

### 8.17 Edge cases and test vectors

* Plane sizes not multiple of 8 (w, h = 1, 7, 9, 15), single-pixel planes, a chunk where a sub-sampled LOSSY_DCT channel
  has n_c = 0, sub-sampled CSC sets (xSampling = ySampling = 2).
* CSC set with mixed HALF/FLOAT channels; two layers (`a.R a.G a.B b.R b.G b.B`) → two sets, in prefix order; R and G
  without B; a lowercase `r` channel (v2: UNKNOWN; legacy v0/v1 data: LOSSY_DCT).
* pLinear R alone (no curve) versus pLinear R, G, B (curve applied anyway).
* UINT A (RLE with 4 byte planes), HALF A, FLOAT A; a UINT R (UNKNOWN).
* Blocks with lnz = 0 (DC-only shortcut), lnz = 1 with a literal 0x0000 (full IDCT), lnz in each of the ranges
  {1}, {2}, {3…8}, {9}, {10…19}, {20}, {21…34}, {35…63}; a run symbol jumping past 63; AC literal 0x8000.
* Version 0/1 chunks (no rule block), acCompression = 1 in a scan-line file, an empty rule block.
* Coefficient overflow (DC 0x7BFF with large AC) → Inf → toLinear gives 0.
* Test vector (single component, natural-order output of 8.10, then toLinear): DC = 0x4800 (8.0), no AC → every pixel
  0x3C00. Coefficients z0 = 0x4800, z1 = 0x3C00, z2 = 0xBC00, z5 = 0x3800, z63 = 0x3400, others 0 (lnz = 63), after the
  full inverse DCT, binary16 rounding and toLinear, rows top to bottom:
  ```
  3cd0 3c02 3a5e 3878 37d5 36f3 37c6 3808
  3d00 3c7e 3a2c 3973 375c 3840 37c9 387a
  3dc7 3c80 3c16 38f2 3967 37ec 3922 38e5
  3e56 3df4 3ba3 3ba0 38c0 3a1b 3926 3a20
  3fc5 3df7 3d8f 3ac6 3ba0 399e 3b3d 3ad5
  4040 3fdf 3d33 3d00 3ad7 3c16 3b3f 3c26
  40ec 3fe3 3ec9 3c9f 3c9c 3bc1 3c7e 3c74
  411a 406e 3e99 3d6a 3c6a 3c7b 3c80 3cd0
  ```
  (computed from this description with the constants above, not by the OpenEXR binary: confirm against the reference
  tool before relying on it.)

---

## 9. Luminance / chroma images (Y, RY, BY)

* Convention of the RGBA convenience layer, not of the compression codecs: a "luminance/chroma" image stores
  `Y` (HALF, sampling 1×1), and optionally `RY` and `BY` (HALF, sampling 2×2, pLinear set), plus optionally `A`.
  A layer prefix ("name.") may precede these names.
* `Y = wR·R + wG·G + wB·B`, with weights derived from the `chromaticities` attribute (Rec. 709 primaries / D65 by default)
  as the Y row of the RGB→XYZ matrix normalised to sum 1. Before conversion, non-finite or negative R, G, B are set
  to 0. If R = G = B, then Y = G and RY = BY = 0 exactly (so grey images round-trip losslessly); otherwise
  `RY = (R − Y) / Y`, `BY = (B − Y) / Y`, each set to 0 when `|R − Y|` (or `|B − Y|`) is not below `HALF_MAX × Y`.
* Reconstruction: chroma is upsampled to full resolution with fixed 27-tap filters horizontally and vertically; then
  if RY = BY = 0, R = G = B = Y exactly; otherwise `R = (RY + 1)·Y`, `B = (BY + 1)·Y`, `G = (Y − wR·R − wB·B) / wG`,
  followed by a saturation clamp. Writers additionally round Y to 7 and chroma to 5 mantissa bits by default.
* **Y-only image** (Y present, no RY/BY): a grayscale image. Readers present it as R = G = B = Y (alpha from A if present,
  else 1). For our purposes, decode Y as a single gray channel.

---

## 10. Type conversions applied when the requested type differs from the file

These are reader-side conversions (not part of the codecs); listed for consistency with OpenEXR:

* HALF → FLOAT: exact.
* FLOAT → HALF: round to nearest, ties to even; |x| ≥ 65520 → ±Inf; results below the smallest denormal round to ±0
  (2^−25 exactly ties to 0); NaN keeps the sign and the top 10 mantissa bits, forced non-zero (set the lowest bit if they
  were all 0).
* UINT → HALF: values > 65504 → +Inf (0x7C00); otherwise exact conversion to float then FLOAT → HALF.
* UINT → FLOAT: binary32 rounding of the integer (round to nearest even).
* HALF → UINT: negative (including −0.0) or NaN → 0; +Inf → 4294967295; otherwise truncation toward zero.
* FLOAT → UINT: negative or NaN → 0; +Inf or > 4294967296.0 → 4294967295; otherwise truncation toward zero.
* Channels absent from the file are filled with the fill value requested by the caller (the RGBA layer uses 0 for
  colour, 1 for alpha).

---

## 11. Summary of fallback / raw rules

| compression | decoder: P = S | reference encoder stores compressed when | recommended encoder rule |
|---|---|---|---|
| RLE | raw | compressed < S | compressed < S |
| ZIPS / ZIP | raw | compressed < S | compressed < S |
| PIZ | raw | compressed < S | compressed < S |
| PXR24 | raw | compressed ≤ S (Core quirk) | compressed < S |
| B44 / B44A | raw | compressed ≤ S (Core quirk) | compressed < S |
| DWAA / DWAB | raw | compressed < S | compressed < S |
| HTJ2K256 / HTJ2K32 / LJ2K | raw | compressed ≤ S (Core quirk, 14.7) | compressed < S |
| ZSTD | raw (a payload without the `zstd-exr` magic is an error unless P = S) | compressed < S | compressed < S |

Deep chunks apply the rule separately to the sample count table and to the sample data (1.6).

---

## 12. Points of residual uncertainty

* B44 pLinear tables rely on binary32 `exp`/`ln` of the platform's math library; the hashes above match both macOS libm
  and correctly rounded double→float computation. A platform whose `expf`/`logf` are less accurate could in principle
  differ by one half ULP on rare entries. Recommended: ship the tables as data and verify them against the hashes.
* The reference Core library could not be compiled in this environment (policy), so the table hashes were computed
  from the formula, not from OpenEXR's own binary.
* Huffman: behaviour on incomplete or length-58 codes differs between the two reference decoders; the constraints in
  5.4.3 avoid both problems.
* The 14-bit wavelet inverse for corrupt data (5.3.4) differs between the classic definition and the v3.5 Core; valid
  data is unaffected.
* DWA (section 8): the arithmetic was read from the scalar code; the inverse-DCT and colour constants come from the
  platform `cosf` folded at compile time, and match the correctly rounded values (checked on macOS arm64 and with
  binary64 `cos`); a toolchain folding `cosf` differently would change them, which is why their bit patterns are given.
  The 8×8 test vector of 8.17 was computed from this description, not by running OpenEXR (it was not built in this
  environment); confirm it against the reference tool.
* DWA perceptual tables (8.13) are built at run time with the platform's `powf`/`logf`; the hashes match macOS libm and
  correctly rounded results, but a less accurate libm (for example on Windows or an older glibc) could differ on rare
  entries, so the reference itself may decode slightly differently on such platforms.
* DWA NaN handling differs between the reference's x64 (scalar conversions) and arm64 (hardware conversions, other
  default NaN) builds (8.11); only NaN payloads and signs are affected, and only for pLinear single channels.
* DWA quantisation (8.14 step 7) is a heuristic in the reference; our encoder is not expected to reproduce its exact
  choices. The decoder is unaffected.
* ZSTD (13): the container was read from 3.5.2 only; ZSTD first shipped in 3.5.0 and the shallow history did not show
  whether 3.5.0/3.5.1 wrote a different layout (the 3.5.2 reader accepts formats 1 and 2 only).
* HTJ2K (14): the document gives the OpenEXR wrapper, the component mapping and the codestream parameters, not the
  HTJ2K block coder itself (ITU-T T.814) nor the JPEG 2000 Part 1 machinery; the NLT type 3 definition follows OpenJPH's
  implementation. LJ2K (14.6) is not described bit-exactly (OpenJPH floating-point 9/7 and an experimental non-standard
  NLT type 4).
* Deep tiled sample tables (1.6): the full-tile raw size `Pt = T` written by the C++ writer for clipped tiles was read
  from the writer; the reference reader's failure on such a chunk with a compression other than NONE was deduced from
  the reader code, not observed.
* The deep compositing description (1.7) covers the default compositing of the C++ helper; a user-supplied compositor
  can do anything. Its NaN ordering is unspecified.

---

## 13. ZSTD (id 13)

Sections 13 and 14 were added after the first version of this document; they are placed after section 12 so that
the section numbers cited by the code stay valid.

ZSTD is lossless, **1 line per scan-line chunk** (one tile per tiled chunk), and is allowed in deep parts. It was
introduced in OpenEXR 3.5.0; there is **no Blosc/Blosc2 container**: OpenEXR uses its own 24-byte prefix, its own byte
reordering and the zstd library directly.

### 13.1 Payload layout

| offset | size | field |
|---|---|---|
| 0 | 8 | magic: the ASCII bytes `zstd-exr` (`7A 73 74 64 2D 65 78 72`) |
| 8 | u32 LE | format: 1 or 2 |
| 12 | u32 LE | flags: format 1 → must be 0; format 2 → only bit 0 ("delta after sort") may be set |
| 16 | u64 LE | N: byte length of the Zstandard data that follows |
| 24 | N | Zstandard data (RFC 8878) |

`24 + N` must equal P exactly. Decoding the Zstandard data gives the **inner stream** (13.2). The delta step (13.3)
is applied if and only if format = 2 and flag bit 0 is set.

Zstandard constraints: no dictionary (the reference decoder loads none, so a frame that needs a dictionary fails);
the reference writes one frame (with the content size in the frame header, no checksum, single-segment when the
library chooses it); the reference decoder uses the general one-shot decompression, which also accepts several
concatenated frames, skippable frames, an absent content size and a checksum (verified when present). The decoded
size must not exceed `S + 16` (the reference rejects a frame whose declared content size is larger, and bounds its
output buffer to that size otherwise).

### 13.2 Inner stream

One or two **segments**, back to back, each: a u64 LE length L, then L bytes. After the last segment the inner stream
must be exhausted (no trailing bytes).

Let **H** = the number of bytes of the "sorted" buffer (13.3) that belong to 2-byte channels (HALF), and S the chunk's
uncompressed size; `S − H` bytes belong to 4-byte channels (FLOAT and UINT, which are not distinguished).
* H > 0 and S − H > 0: two segments, the first of length H with element size 2, the second of length S − H with element
  size 4.
* Otherwise one segment of length S, element size 4 if H = 0, else 2.

Each segment length must equal the expected length exactly. Each segment holds the corresponding bytes of the
sorted buffer **byte-shuffled**: with element size e and `m = L / e` elements, the segment is e planes of m bytes; plane
j holds byte j of every element, in element order (for e = 2: all first bytes, then all second bytes; for e = 4: four
planes). L is always a multiple of e.

### 13.3 Sorted buffer and delta (flat scan-line and tiled chunks)

The sorted buffer is a permutation of U (1.3/1.4): first, for each line of the chunk top to bottom, for each
**2-byte** channel in header order, that channel's samples on that line (if the channel has a line there, 1.2); then,
for each line top to bottom, for each **4-byte** channel in header order, its samples on that line. Each (line,
channel) piece is copied unchanged (values stay little-endian). So H = total bytes of the HALF channels.

**Delta** (format 2 with flag bit 0): each (line, channel) piece of n samples is replaced, in place in the sorted
buffer, by its first value followed by the differences `x_i − x_{i−1}` (i = 1…n−1) computed on the u16 (2-byte channels)
or u32 (4-byte channels) little-endian values modulo 2^16 / 2^32. The predictor restarts at every piece. Decoder: running
sum modulo 2^16 / 2^32 from the first value.

Decoder order: Zstandard decode → split segments and unshuffle into the sorted buffer → undo the delta (if signalled)
→ un-sort into U.

### 13.4 Deep chunks

The same container and inner stream, with two different "channel/line" grids (1.6 gives the deep buffers):
* **Sample count table** (S = `w × h × 4`, w and h the clipped chunk size, see 1.6): treated as a single 4-byte channel with w samples on each of the h lines.
  The sorted buffer equals the table; one segment, element size 4.
* **Sample data:** every channel has `n_row` samples on row r, where `n_row` is the row total (the last cumulative entry
  of that row in the decoded sample count table). The sorted buffer is: for each row, for each 2-byte channel in header
  order, its `n_row` samples; then for each row, for each 4-byte channel, its `n_row` samples. Segments and delta as above.

The decoder must know which buffer it is decoding (the table is decompressed first and its row totals are needed to
decode the data). A deep data chunk with Sd = 0 has nothing to decode.

### 13.5 Encoder

* Reference **[encoder choice]**: flat chunks (scan line, tiled) are written as **format 2, flags 1** (with delta);
  deep sample tables and deep sample data as **format 1, flags 0** (no delta). A decoder must accept either format for
  any kind of chunk, and format 2 with flags 0 (no delta).
* Compression level: the writer's zstd level, 1…22 (values outside are clamped), default **5**. Not stored in the file.
* Fallback: the reference writes the chunk raw when `24 + N ≥ S` (compressed only when strictly smaller), so the raw
  rule of 1.5 holds. **Decoder:** if P = S the bytes are raw (1.5). Otherwise the payload must start with the magic; a
  payload shorter than 24 bytes or with another magic is an error (the reference only accepts the "no magic" case when
  P = S).
* An encoder that wants the smallest interoperable output may use any Zstandard frame parameters (window size, levels,
  long distance matching) that the reference zstd 1.5.7 decoder accepts with its default limits (window ≤ 2^27 bytes);
  a window no larger than the content is the natural choice for chunk-sized inputs.

### 13.6 Edge cases

Only HALF channels (one segment, e = 2); only FLOAT/UINT channels (one segment, e = 4); mixed; a sub-sampled channel
absent from a chunk; width 1 (delta of a single value); values that wrap in the delta (0xFFFF then 0x0000); deep chunks
with rows of 0 samples; a deep data chunk with HALF and FLOAT channels; P = S (raw); P ≠ S without the magic (error);
format 1 with flags ≠ 0, format 3 (errors); trailing bytes in the inner stream (error); `24 + N ≠ P` (error).

## 14. HTJ2K256 (id 10), HTJ2K32 (id 11) and LJ2K (id 12)

All three code each chunk as an independent JPEG 2000 codestream (ITU-T T.800 / ISO/IEC 15444-1) using the
High-Throughput block coder (ITU-T T.814 / ISO/IEC 15444-15), behind a small OpenEXR header. The reference codec is
**OpenJPH 0.32.0** (vendored in the OpenEXR 3.5.2 release, built without SIMD by the reference script; the lossless
path is integer-only, so SIMD would not change its output anyway).

| id | name | lines per scan-line chunk | coding |
|---|---|---|---|
| 10 | HTJ2K256 | 256 | lossless (reversible 5/3, RCT) |
| 11 | HTJ2K32 | 32 | lossless, identical to HTJ2K256 apart from the chunk height |
| 12 | LJ2K | 256 | lossy on an R, G, B triplet (irreversible 9/7, ICT), lossless on every other channel |

Tiled parts: one tile per chunk. Not allowed in deep parts.

### 14.1 Chunk payload

All header integers are **big-endian**:

| field | size | content |
|---|---|---|
| MAGIC | u16 BE | 0x4854 (`'H' 'T'`) for HTJ2K256 and HTJ2K32; 0x484C (`'H' 'L'`) for LJ2K written by 3.5.2 and later |
| PLEN | u32 BE | byte length of the header payload that follows |
| NCH | u16 BE | number of entries of the channel map (first field of the payload) |
| CS_TO_F | NCH × u16 BE | entry i = index (in header order) of the EXR channel coded as codestream component i |
| extension | PLEN − 2 − 2·NCH bytes | opaque, skipped by the reader; the writer writes none (PLEN = 2 + 2·NCH) |
| codestream | the rest of the chunk | starts at offset 6 + PLEN, must not be empty |

Decoder checks: magic 0x4854, or (LJ2K only) 0x484C; `6 + PLEN ≤ P`; the CS_TO_F entries are all < NCH and pairwise
distinct (a permutation); NCH = number of channels of the part; the codestream has NCH components. **[decoder
robustness]** The reference parses this header only for the first chunk decoded by a given decoding context and
reuses the map and the header length for later chunks with the same channel count; every chunk of a part therefore must
carry the same header (the reference writer always does, since it depends only on the channel list).

### 14.2 Channel map and RGB detection

The writer looks for an RGB triplet. For each channel in header order, with suffix = the part of the name after the
last `.` (the whole name if none) and prefix = the part before that `.` (empty if none), it tries two candidate
triplets in order: (`r`, `g`, `b`) then (`red`, `green`, `blue`), suffixes compared **ASCII case-insensitively**. A
candidate triplet remembers the prefix of the first channel assigned to it; a channel whose prefix differs (byte
comparison, case-sensitive, same length) skips that triplet. Within a triplet, the channel takes the R slot if its
suffix matches the R name and the slot is empty, else the G slot likewise, else the B slot; a channel stops at the
first triplet that takes it. A triplet is an RGB set when its three slots are filled and the three channels have the
same pixel type, xSampling and ySampling; the first triplet (`r g b` before `red green blue`) that qualifies is used.

* RGB set found: components 0, 1, 2 = the R, G, B channels; the other channels follow as components 3, 4, … in header
  order.
* Otherwise: component i = channel i (identity map).

The map is written in the header; the decoder uses the header's map, not its own detection.

### 14.3 Components and sample values

For component i coding EXR channel c = CS_TO_F[i]:
* sub-sampling (XRsiz, YRsiz) = (xSampling, ySampling) of c;
* precision / signedness: HALF → 16-bit **signed**; FLOAT → 32-bit **signed**; UINT → 32-bit **unsigned**;
* sample values: HALF → the binary16 bit pattern read as an i16 (sign-extended); FLOAT → the binary32 bit pattern read
  as an i32; UINT → the u32 value. For the unsigned components, JPEG 2000's usual DC level shift (subtract 2^31 before
  coding, add it after decoding) applies.
* **NLT** (non-linearity point transform, marker 0xFF76, from ISO/IEC 15444-2 as used by HTJ2K): every HALF and FLOAT
  component coded reversibly carries the **type 3, "binary complement"** non-linearity; UINT components have none. For
  a signed B-bit component, the codestream-domain value v and the sample value s are related by `s = v` when v ≥ 0, and
  `s = −v − 2^(B−1) − 1` when v < 0 (the same formula maps s to v: it is an involution). It turns the sign-magnitude
  ordering of IEEE bit patterns into a monotonic integer ordering (a negative float with magnitude bits m becomes −m − 1).
  In the decoder it is applied after the inverse colour transform; compute it in at least 33-bit (64-bit) integers for
  B = 32.
* Marker details produced by the reference: Rsiz = 0x4000 (HT) | 0x8000 (extensions) | 0x0200 (NLT) when any NLT is
  present (0x4000 alone when every channel is UINT); one NLT marker segment per HALF/FLOAT component, in the main
  header: marker 0xFF76, Lnlt = 6 (u16), Cnlt = component index (u16; 65535 would mean all components, not used by
  the writer), BDnlt (u8: precision − 1, | 0x80 when signed: 0x8F for HALF, 0x9F for FLOAT), Tnlt (u8: 3). A decoder
  must also honour an all-components segment (Cnlt = 65535) and per-component segments overriding it.

### 14.4 Codestream parameters (reference encoder) and decoder requirements

Reference encoder, lossless (HTJ2K256, HTJ2K32, and the non-lossy components of LJ2K):
* image origin (0, 0), image extent = chunk width × chunk height, **one tile** covering the image (no tiling);
* COD: reversible 5/3 wavelet, **5 decomposition levels**, code blocks **128 wide × 32 high** (xcb′ = 5, ycb′ = 3),
  code-block style 0x40 (HT), progression order **RPCL**, 1 quality layer, no precinct partition (maximal precincts),
  no SOP/EPH;
* multi-component transform: the reversible colour transform (RCT) on components 0–2 **if and only if** an RGB set was
  detected **and no channel of the part is sub-sampled**; any sub-sampled channel disables the transform (the codestream
  is then coded component by component);
* markers: SOC, SIZ, CAP (HT capabilities), COD, QCD (reversible, no quantization), NLT, a TLM marker (tile-part lengths),
  then the single tile split into **one tile-part per resolution** (6 tile-parts for 5 levels), EOC. COM only for LJ2K
  padding (14.6).

What the reference decoder (OpenJPH) requires of a codestream, so what our encoder must respect: HT code blocks (OpenJPH
does not decode the original Part 1 block coder); image extent equal to the chunk size exactly (lossless) and image
origin (0, 0); NCH components; for each component, `ceil(W / XRsiz) = width/xSampling` and `ceil(H / YRsiz) = n_c`
of the channel in this chunk (the reference checks both widths/heights and also `W / XRsiz` and `H / YRsiz` in integer
division). Any other valid choice (levels, block size, progression, precincts, layers, markers) is decodable by OpenJPH
within its limits.

Sub-sampled channels: the codestream's component origin is line 0 of the chunk, so component lines are the chunk lines
0, YRsiz, 2·YRsiz, …; this matches the EXR channel lines only when the chunk's first line y0 satisfies
`y0 mod ySampling = 0`. **[encoder choice]** When it does not (for example ySampling 3 with 32-line chunks), or when
n_c differs from `ceil(H / ySampling)`, the reference writer fails or produces a chunk its decoder rejects: store such
chunks raw (P = S).

32-bit components: after the reversible 5/3 transform with 5 levels and the RCT, magnitudes need more than 32 bits;
the HT block decoder must support more than 31 magnitude bit-planes (OpenJPH has a 64-bit path for this).

### 14.5 Mapping codestream lines to U

* **No sub-sampled channel ("interleaved")**: for each line y of the chunk, for each component i in codestream order,
  the decoded line of component i (after the inverse RCT and NLT) is written at the position of channel CS_TO_F[i] in
  line y of U (the channel's byte offset inside the line = Σ of `width × bytesPerSample` of the channels before it in
  header order): HALF as the low 16 bits (u16 LE), FLOAT and UINT as 32 bits (u32 LE).
* **Some channel sub-sampled ("planar")**: for each component i in codestream order, its lines in order go to the lines
  of channel CS_TO_F[i] in the chunk, in increasing y (only the lines where the channel is present, 1.2).

### 14.6 LJ2K (id 12)

Same container (magic 0x484C since 3.5.2), same map, same lossless coding for every component except an RGB set
qualifying for lossy coding: an RGB set was detected (14.2), the three channels are "visual" (suffix r, g, b, red,
green, blue — always true for a detected set), not UINT, and not sub-sampled. Then components 0–2 use the irreversible
9/7 wavelet, the irreversible colour transform (ICT) and scalar quantization, and an OpenJPH-specific **type 4** NLT
("binary complement followed by a look-up table": the 513-point table for HALF, 257 points for FLOAT, built from the
same perceptual curve family as DWA, see 8.13: `x^(1/2.2)` below 1 and `ln(x)/2.2 + 1` above, applied in binary64).
Type 4 is marked experimental by OpenJPH and is not part of a published standard. The coded image is padded by
replicating the last column and row until each dimension n′ satisfies `n′ ≡ 1 (mod 32)` (2^5 for 5 levels), the
padding is declared in a COM marker (text `OpenEXR LJ2K padding: rows=… cols=…`) and is dropped by the decoder (the
reference accepts a padded codestream only for irreversible coding and non-sub-sampled chunks). The quality (default
110, range 1…150, encoder only) selects a Qfactor below 97 and explicit quantization steps above. LJ2K files from 3.5.0
and 3.5.1 use magic 0x4854 and apply the transfer function outside the codestream; the 3.5.2 reader undoes it with fixed
scale factors.

Bit-exact LJ2K decoding requires reproducing OpenJPH's floating-point 9/7 synthesis, ICT, dequantization and its
type 4 table interpolation, which this document does not describe. **Recommended:** treat LJ2K as unsupported, or decode
only the chunks stored raw (P = S).

### 14.7 Raw fallback

The reference writes the codestream into a buffer of `S − (6 + PLEN)` bytes; when it does not fit, the chunk is stored
raw. A chunk whose header plus codestream is **exactly** S bytes is therefore written compressed by the Core library and
then misread as raw by every reader (the same quirk as PXR24 and B44 in 1.5; the C++ writer adds its own
"compressed only if strictly smaller" check). Our encoder must store compressed only when strictly smaller than S.

### 14.8 Edge cases

* RGB detection: `R G B` (case-insensitive: `r g B`), `red green blue`, layer prefixes (`main.b main.g main.r` is
  detected and reordered to R, G, B), `MainR` (not detected), R/G/B of different types or samplings (not detected,
  identity map), two layers with RGB (only the first prefix that a triplet records is used).
* A part with an RGB set and one sub-sampled alpha: no RCT.
* HALF values with the sign bit set (NLT), −0.0, NaN payloads (lossless: must round-trip bit-exactly), FLOAT and UINT
  extremes (0, 0xFFFFFFFF, −FLT_MAX), widths and heights 1, chunks of 1 line (last chunk), tiles clipped at the edges.
* Header errors: wrong magic, PLEN too large, duplicate map entries, NCH ≠ channel count, empty codestream.
