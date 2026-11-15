# zxing-cpp sample images

The directories are the test samples of zxing-cpp (`test/samples`), at commit
`f19f7c6e0509fe0eeb822bf35c41b0f637dec290` of https://github.com/zxing-cpp/zxing-cpp, under the Apache License 2.0
(`LICENSE`). Many of them are photographs taken with phones and scanners, inherited from the test suite of ZXing: blur,
perspective, uneven lighting, curved paper, glare and low resolution.

Each image has the expected text of its symbols in a `.txt` file of the same name (without the suffixes `-1`, `_b` or
`!`), or in a `.toml` file that lists several symbols and their properties. `!defaults.toml` gives the defaults of a
directory, among which `find`: the rotations zxing-cpp reads the images at (`sh`: 0° and 180°, `sa`: the four
rotations). A name that ends with `!` is an image that zxing-cpp does not read.

The directories of the symbologies that the readers do not support (MaxiCode, MicroPDF417, Telepen, DX film edge) are
not copied. `ZXingCppSampleTests` reads every image and compares the outcome with `../ZXingCppSamples.Results`.
