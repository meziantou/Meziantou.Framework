// Decodes every displayed frame of an image with Apple ImageIO (CGImageSource) and writes them to stdout as tightly
// packed, top-down, straight-alpha RGBA: 8-bit samples, or 16-bit little-endian samples when the decoded frames are
// 16-bit. Used by tools/Meziantou.Framework.Imaging.CorpusGenerator/GoldenCorpus.cs as an independent APNG and GIF cross-check (never by the tests).
// Fails instead of converting when ImageIO returns another layout (premultiplied alpha, other channel orders), so the
// cross-check never compares data that went through an extra conversion.
// With --binary-alpha (GIF: every pixel is opaque or fully transparent), two more 8-bit layouts are accepted because they
// convert losslessly: "none skip last" (an opaque frame: the padding byte is replaced by 255) and "premultiplied last" when
// every alpha sample is 0 or 255 (opaque colors are unchanged; fully transparent pixels have no color, written as 0,0,0,0).
// Any other alpha value still fails.
// Usage: swift imageio_frames.swift [--binary-alpha] <input> (prints "frames <n> <width> <height> <bits>" on stderr)
import Foundation
import ImageIO

var arguments = Array(CommandLine.arguments.dropFirst())
let binaryAlpha = arguments.first == "--binary-alpha"
if binaryAlpha {
    arguments.removeFirst()
}

func fail(_ message: String, _ code: Int32) -> Never {
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(code)
}

let url = URL(fileURLWithPath: arguments[0])
guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else {
    fail("cannot open input", 2)
}

let count = CGImageSourceGetCount(source)
var output = Data()
var description = ""
for index in 0..<count {
    guard let image = CGImageSourceCreateImageAtIndex(source, index, nil), let data = image.dataProvider?.data as Data? else {
        fail("cannot decode frame \(index)", 3)
    }

    let bits = image.bitsPerComponent
    let order = image.bitmapInfo.rawValue & CGBitmapInfo.byteOrderMask.rawValue
    let expectedOrder = bits == 16 ? CGBitmapInfo.byteOrder16Little.rawValue : 0
    let channels = image.bitsPerPixel / bits
    let orderMatches = order == expectedOrder || (bits == 8 && (order == CGBitmapInfo.byteOrder32Big.rawValue || order == CGBitmapInfo.byteOrder16Big.rawValue))
    let binaryLayout = binaryAlpha && bits == 8 && channels == 4 && orderMatches && (image.alphaInfo == .noneSkipLast || image.alphaInfo == .premultipliedLast)
    guard binaryLayout || (image.alphaInfo == .last && (channels == 4 || channels == 2) && (bits == 8 || bits == 16) && orderMatches) else {
        fail("unsupported decoded layout: bits \(bits), alpha \(image.alphaInfo.rawValue), bitmap \(image.bitmapInfo.rawValue)", 4)
    }

    let sampleBytes = bits / 8
    for y in 0..<image.height {
        let row = data.subdata(in: (y * image.bytesPerRow)..<(y * image.bytesPerRow + image.width * channels * sampleBytes))
        if binaryLayout {
            var converted = [UInt8](row)
            for x in 0..<image.width {
                let alpha = image.alphaInfo == .noneSkipLast ? 255 : converted[x * 4 + 3]
                guard alpha == 0 || alpha == 255 else {
                    fail("frame \(index) pixel (\(x), \(y)) has alpha \(alpha): not a binary-alpha frame", 5)
                }

                converted[x * 4 + 3] = alpha
                if alpha == 0 {
                    converted[x * 4] = 0
                    converted[x * 4 + 1] = 0
                    converted[x * 4 + 2] = 0
                }
            }

            output.append(contentsOf: converted)
            continue
        }

        if channels == 4 {
            output.append(row)
            continue
        }

        for x in 0..<image.width {
            let gray = row.subdata(in: (x * 2 * sampleBytes)..<((x * 2 + 1) * sampleBytes))
            let alpha = row.subdata(in: ((x * 2 + 1) * sampleBytes)..<((x * 2 + 2) * sampleBytes))
            output.append(gray)
            output.append(gray)
            output.append(gray)
            output.append(alpha)
        }
    }

    description = "frames \(count) \(image.width) \(image.height) \(bits)\n"
}

FileHandle.standardError.write(description.data(using: .utf8)!)
FileHandle.standardOutput.write(output)
