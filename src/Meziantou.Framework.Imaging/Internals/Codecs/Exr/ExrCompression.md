# OpenEXR chunk compression — clean-room format specification

This document is the only artifact of the clean-room process behind the OpenEXR codec of this library: it was written by
reading the OpenEXR reference library (BSD-3-Clause, see `THIRD-PARTY-NOTICES.md`) and describes the bytes of the
compressed chunks, not the code that produces them. The codec was written from this document and from the OpenEXR file
layout specification, without access to the OpenEXR sources, and cites the sections below by number. Section 8 (DWAA and
DWAB) only scopes work the codec does not do yet: the decoder rejects these compressions.

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
| 10 | HTJ2K256 | 256 | no (out of scope) |
| 11 | HTJ2K32 | 32 | no (out of scope) |
| 12 | LJ2K | 256 | yes (out of scope) |
| 13 | ZSTD | 1 | no (out of scope) |

The lines-per-chunk values for 0–9 asked to be verified are confirmed exactly as listed above.
Ids 10–13 are new in v3.4/3.5 and are not covered here. Any id ≥ 14 is invalid.

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

Deep parts only allow NONE, RLE, ZIPS (and ZSTD). Not detailed further here.

### 1.7 Edge cases to test (common)

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

## 8. DWAA (id 8) and DWAB (id 9) — overview for later scoping

DWAA and DWAB share one byte format; they differ in lines per chunk (32 vs 256). A full specification would be
roughly as large as everything above combined (DCT, quantization tables, colour transform, perceptual curve tables, AC
run-length format, three sub-compressors). Structure of a chunk payload:

1. **Header: 11 × u64 LE** in this order: version (0, 1 or 2; writers emit 2; > 2 is an error), unknown-uncompressed-size,
   unknown-compressed-size, AC-compressed-size, DC-compressed-size, RLE-compressed-size, RLE-uncompressed-size,
   RLE-raw-size, AC-uncompressed-count (number of u16), DC-uncompressed-count (number of u16), AC-compression
   (0 = static Huffman, 1 = deflate).
2. **Version ≥ 2: channel classification rules**: u16 LE total byte size of the rule block (including these 2 bytes),
   then rules, each: NUL-terminated channel-name suffix (≤ 128 chars), one byte
   `((cscIndex + 1) << 4) | (scheme << 2) | caseInsensitive` (cscIndex −1…2 = none/R/G/B; scheme 0 = UNKNOWN (zlib),
   1 = LOSSY_DCT, 2 = RLE), one byte pixel type. Versions 0/1 use a fixed legacy rule set
   (case-insensitive r, red, g, grn, green, b, blu, blue, y, by, ry → DCT; a → RLE). The default v2 rules written by the
   reference: R, G, B (with colour-transform indices 0/1/2), Y, BY, RY → LOSSY_DCT for HALF and FLOAT; A → RLE for UINT, HALF,
   FLOAT; everything else UNKNOWN. A channel is classified by the part of its name after the last '.' and by its type.
3. Data sections, in order: UNKNOWN (zlib of the planar raw data of unclassified channels), AC (Huffman — same stream
   format as PIZ section 5.4 — or zlib of the u16 AC values), DC (section 2 byte transform + zlib of the u16 DC values,
   grouped per component plane), RLE (section 3 byte RLE of the planar, byte-split alpha data, then zlib).
4. LOSSY_DCT channels: values converted to half, mapped through a perceptual curve table (unless pLinear), optional
   R′G′B′ → Y′CbCr (Rec. 709 weights, chroma zero at black) when R, G, B with a common prefix are present, 8×8 float
   DCT, coefficients stored as half bit patterns in zig-zag order, quantized by picking the value with fewest set bits
   within a per-coefficient error derived from JPEG tables × `dwaCompressionLevel / 100000` (header attribute, default
   45). AC stream per block: values, where a u16 of the form 0xFFnn means "nn zeros" and 0xFF00 means end of block.
   Decoding needs the inverse DCT, the inverse colour transform and the inverse perceptual table bit-exactly to match
   OpenEXR, which involves float arithmetic order — this is the main risk for a later effort.
5. The reference writes DEFLATE AC compression for tiled DWAA parts and static Huffman otherwise; readers must honour the
   header field. Fallback to raw when output ≥ S.

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
* DWA (section 8) is an overview only and not sufficient for bit-exact decoding.
