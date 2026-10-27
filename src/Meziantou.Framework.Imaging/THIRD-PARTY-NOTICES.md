# Third-Party Notices

This project includes third-party software components. The original copyright
notices and the licenses under which the respective authors made such components
available are reproduced below.

---

## libjxl

- Project: libjxl, the reference implementation of JPEG XL
- Source: <https://github.com/libjxl/libjxl>
- License: <https://github.com/libjxl/libjxl/blob/v0.12.0/LICENSE>
- Version: 0.12.0 (commit a7a9c787341cf703dede03c2009fa460cae5e5df)
- Used for: the adaptive quantization heuristic of the lossy JPEG XL encoder
  (`Internals/Codecs/JpegXl/Encoding/JpegXlAdaptiveQuantization.cs`), ported from
  `lib/jxl/enc_adaptive_quantization.cc` and `lib/jxl/quantizer.cc`; the SSIMULACRA 2 metric
  (`Internals/Processing/Ssimulacra2Calculator.cs`), ported from `tools/ssimulacra2.cc` and
  `tools/gauss_blur.cc`; the Butteraugli metric (`Internals/Processing/Butteraugli*.cs`), ported from
  `lib/jxl/butteraugli/butteraugli.cc`, `lib/jxl/enc_comparator.cc` and `lib/extras/metrics.cc`; and the
  sRGB curve and cube root approximations they use (`Internals/Processing/LinearRgbLoader.cs`,
  `lib/jxl/cms/transfer_functions-inl.h`, `lib/jxl/base/fast_math-inl.h`).

```
Copyright (c) the JPEG XL Project Authors.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## OpenEXR

- Project: OpenEXR, the reference implementation of the OpenEXR image format
- Source: <https://github.com/AcademySoftwareFoundation/openexr>
- License: <https://github.com/AcademySoftwareFoundation/openexr/blob/v3.5.2/LICENSE.md>
- Version: 3.5.2 (commit 69b2604fc76e370615438bdc8d2cd95b9349c12e)
- Used for: the description of the chunk compressions of the OpenEXR codec
  (`Internals/Codecs/Exr/ExrCompression.md`), written by reading the library. No code was copied: the codec
  (`Internals/Codecs/Exr/`) was written from that description.

```
Copyright (c) 2004, Industrial Light & Magic, a division of Lucasfilm
Entertainment Company Ltd.  Portions contributed and copyright held by
others as indicated.  All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

    * Redistributions of source code must retain the above
      copyright notice, this list of conditions and the following
      disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided with
      the distribution.

    * Neither the name of Industrial Light & Magic nor the names of
      any other contributors to this software may be used to endorse or
      promote products derived from this software without specific prior
      written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS
IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
