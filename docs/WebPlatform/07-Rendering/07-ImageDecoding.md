---
title: Image decoding
description: Decode the frames of still and animated images with the ImageDecoder of WebCodecs, from .NET or from the scripts of a page.
---

The pages decode images with `ImageDecoder`, the image decoding API of [WebCodecs](https://w3c.github.io/webcodecs/#image-decoding).
With a rendering backend, it decodes the formats of the `img` elements: PNG (and animated PNG), JPEG, GIF, WebP, AVIF,
JPEG XL, BMP and ICO. .NET can use it too:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Imaging.Tests/ImageDecodingTests.cs;region=WebPlatformImaging_ImageDecoder;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions { RenderingBackend = new ImagingRenderingBackend() });
context.Navigate("https://example.com/", _ => "<p>Animation</p>");
context.EventLoop.RunUntilIdle();

// A 2x2 GIF of 3 frames that plays once: red (200 ms), transparent (200 ms), green
var gif = Convert.FromBase64String("R0lGODlhAgACAPEAAAAAAP8AAAD/AAAA/yH5BAkUAAAALAAAAAACAAIAAAIDjKMAACH5BAkUAAIALAAAAAACAAIAAAIDlKUAACwAAAAAAgACAAACA5SlAAA7");
var decoder = new WebCodecs.ImageDecoder(context.Window, new WebCodecs.ImageDecoderInit { Type = "image/gif", Data = gif });

// The decoder works in the tasks of the event loop of the window
var decoding = decoder.Decode(new WebCodecs.ImageDecodeOptions { FrameIndex = 1 });
context.EventLoop.RunUntilIdle();

var track = decoder.Tracks.SelectedTrack!;
Assert.True(track.Animated);
Assert.Equal(3u, track.FrameCount);
Assert.Equal(0f, track.RepetitionCount);

// The frame: its timestamp and duration are in microseconds, its pixels are RGBA
var frame = (await decoding).Image;
Assert.Equal(200_000L, frame.Timestamp);
Assert.Equal(200_000UL, frame.Duration);
Assert.Equal(WebCodecs.VideoPixelFormat.RGBA, frame.Format);

var pixels = new byte[frame.AllocationSize()];
var copy = frame.CopyTo(pixels);
context.EventLoop.RunUntilIdle();
await copy;
Assert.Equal([0, 0, 0, 0], pixels[..4]);
frame.Close();
decoder.Close();
```
<!-- endref -->

- The decoder works in the tasks of the event loop of its window or worker: run the event loop to get the results of
  [Decode](cref:Meziantou.Framework.WebPlatform.WebCodecs.ImageDecoder.Decode), of
  [Tracks.Ready](cref:Meziantou.Framework.WebPlatform.WebCodecs.ImageTrackList.Ready) and of
  [CopyTo](cref:Meziantou.Framework.WebPlatform.WebCodecs.VideoFrame.CopyTo). The requests are decoded in order.
- The data is a `byte[]` or a `ReadOnlyMemory<byte>` from .NET. The scripts also give a `ReadableStream`: the frames are
  decoded as their data arrives, and the frame count of the track grows with it.
- The decoded frames are [VideoFrame](cref:Meziantou.Framework.WebPlatform.WebCodecs.VideoFrame) objects of RGBA pixels.
  The EXIF orientation of the image is their [Rotation](cref:Meziantou.Framework.WebPlatform.WebCodecs.VideoFrame.Rotation)
  and their [Flip](cref:Meziantou.Framework.WebPlatform.WebCodecs.VideoFrame.Flip): `drawImage()` and
  `createImageBitmap()` apply them, and the display size is the rotated size.
- `colorSpaceConversion: "none"` keeps the encoded colors, and `desiredWidth` and `desiredHeight` scale the frames.

## Limitations

- An image has one track. An AVIF image that has both a still image and an image sequence has the track of the sequence.
- The frames are always RGBA: the YUV formats (`I420`, `I422`, `I444`...) are not used, even for the images encoded in YUV.
- A frame is decoded when its data is complete: `completeFramesOnly: false` does not decode the progressive images
  partially.
- The audio and video codecs of WebCodecs (`VideoDecoder`, `AudioDecoder`, the encoders) and the constructors of
  `VideoFrame` are not implemented.
