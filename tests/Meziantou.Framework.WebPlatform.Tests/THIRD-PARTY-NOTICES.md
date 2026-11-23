# Third-Party Notices

Some tests are ported from the tests of the browsers. The original copyright notices and the licenses under which the
respective authors made them available are reproduced below.

---

## Chromium

- Project: Chromium
- Source: <https://chromium.googlesource.com/chromium/src>
- License: <https://chromium.googlesource.com/chromium/src/+/316ea717ae977fe3663dccd753c96ebbae47ac7c/LICENSE> (BSD 3-Clause License)
- Version: commit [316ea717ae977fe3663dccd753c96ebbae47ac7c](https://chromium.googlesource.com/chromium/src/+/316ea717ae977fe3663dccd753c96ebbae47ac7c) (2026-11-22)
- Files: the `Strict-Transport-Security` headers of `net/http/http_security_headers_unittest.cc`, and whether they are valid
- Used for: `ChromiumValidHeaders` and `ChromiumInvalidHeaders` of `Dom/UpgradeInsecureRequestsTests.cs`

```
// Copyright 2015 The Chromium Authors
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//
//    * Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//    * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the following disclaimer
// in the documentation and/or other materials provided with the
// distribution.
//    * Neither the name of Google LLC nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

---

## WebKit

- Project: WebKit
- Source: <https://github.com/WebKit/WebKit>
- License: the header of `Tools/TestWebKitAPI/Tests/WebKit/WKWebView/HSTS.mm` (BSD 2-Clause License)
- Version: commit [8e12449cbcec79bd5430f136c05140a815e2daf9](https://github.com/WebKit/WebKit/commit/8e12449cbcec79bd5430f136c05140a815e2daf9) (2026-11-22)
- Files: the scenarios of the `Basic`, `ThirdPartyFetch`, `ThirdParty` and `CrossOriginRedirect` tests of `Tools/TestWebKitAPI/Tests/WebKit/WKWebView/HSTS.mm`
- Used for: the `Hsts_WebKit_*` tests of `Dom/UpgradeInsecureRequestsTests.cs`

```
Copyright (C) 2021 Apple Inc. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:
1. Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY APPLE INC. AND ITS CONTRIBUTORS ``AS IS''
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL APPLE INC. OR ITS CONTRIBUTORS
BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF
THE POSSIBILITY OF SUCH DAMAGE.
```
