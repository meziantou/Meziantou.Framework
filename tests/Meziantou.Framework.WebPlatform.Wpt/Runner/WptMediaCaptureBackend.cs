using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The capture devices of the tests (<see cref="BrowsingContextOptions.MediaCapture"/>), as the fake devices of the browsers
/// (e.g. <c>--use-fake-device-for-media-stream</c> in Chromium): a camera that draws a deterministic pattern, a microphone
/// that plays a tone, and a screen, a window and a tab for <c>getDisplayMedia()</c>. The media only depend on the virtual
/// clock of the test.
/// </summary>
internal sealed class WptMediaCaptureBackend : MediaCaptureBackend
{
    /// <summary>The frequency of the tone of the microphone, in Hz.</summary>
    public const double ToneFrequency = 440;

    /// <summary>The amplitude of the tone of the microphone.</summary>
    public const float ToneAmplitude = 0.5f;

    private static readonly MediaCaptureDevice Microphone = new()
    {
        Id = "fake-microphone",
        Kind = MediaDeviceKind.AudioInput,
        Label = "Fake Default Audio Input",
        GroupId = "fake-audio-device",
        Capabilities = new MediaTrackCapabilities
        {
            SampleRate = new ULongRange { Min = 48000, Max = 48000 },
            SampleSize = new ULongRange { Min = 16, Max = 16 },
            ChannelCount = new ULongRange { Min = 1, Max = 2 },
            Latency = new DoubleRange { Min = 0.01, Max = 0.01 },
            EchoCancellation = [true, false, "all", "remote-only"],
            AutoGainControl = [true, false],
            NoiseSuppression = [true, false],
            VoiceIsolation = [true, false],
        },
        DefaultSettings = new MediaTrackSettings
        {
            SampleRate = 48000,
            SampleSize = 16,
            ChannelCount = 1,
            Latency = 0.01,
            EchoCancellation = true,
            AutoGainControl = true,
            NoiseSuppression = true,
            VoiceIsolation = false,
        },
    };

    private static readonly MediaCaptureDevice Camera = new()
    {
        Id = "fake-camera",
        Kind = MediaDeviceKind.VideoInput,
        Label = "fake_device_0",
        GroupId = "fake-video-device",
        Capabilities = new MediaTrackCapabilities
        {
            FacingMode = ["user"],
            ResizeMode = ["none", "crop-and-scale"],

            // The effects of the camera (Media Capture Extensions), which can be turned on and off
            BackgroundBlur = [false, true],
            BackgroundSegmentationMask = [false, true],
            EyeGazeCorrection = [false, true],
            FaceFraming = [false, true],
        },
        DefaultSettings = new MediaTrackSettings
        {
            Width = 640,
            Height = 480,
            FrameRate = 30,
            FacingMode = "user",
            ResizeMode = "none",
            BackgroundBlur = false,
            BackgroundSegmentationMask = false,
            EyeGazeCorrection = false,
            FaceFraming = false,
        },
        VideoModes = [new MediaCaptureVideoMode(640, 480, 30), new MediaCaptureVideoMode(1280, 720, 30), new MediaCaptureVideoMode(1920, 1080, 30)],
    };

    private static readonly MediaCaptureDevice DisplayAudio = new()
    {
        Id = "fake-display-audio",
        Kind = MediaDeviceKind.AudioInput,
        Label = "System Audio",
        DefaultSettings = new MediaTrackSettings
        {
            SampleRate = 48000,
            SampleSize = 16,
            ChannelCount = 2,
            EchoCancellation = false,
            AutoGainControl = false,
            NoiseSuppression = false,
            RestrictOwnAudio = false,
            SuppressLocalAudioPlayback = false,
        },
    };

    public override IReadOnlyList<MediaCaptureDevice> GetDevices() => [Microphone, Camera];

    public override MediaCaptureSource StartCapture(MediaCaptureRequest request)
        => request.Device.Kind == MediaDeviceKind.AudioInput ? new ToneSource(request) : new PatternSource(request);

    public override DisplayCaptureSelection? SelectDisplaySurface(DisplayCaptureRequest request)
    {
        // The "user" picks the surface of the displaySurface constraint, else the screen (a window when the monitors are excluded)
        var surface = request.Options.Video.Constraints?.DisplaySurface switch
        {
            { Bare: { } bare } => bare,
            { BareList: [var first, ..] } => first,
            { Parameters.Ideal: { One: { } one } } => one,
            { Parameters.Ideal: { Many: [var first, ..] } } => first,
            _ => request.Options.MonitorTypeSurfaces == MonitorTypeSurfaces.Exclude ? "window" : "monitor",
        };
        if (surface is not ("monitor" or "window" or "browser"))
        {
            surface = "monitor";
        }

        return new DisplayCaptureSelection(CreateDisplaySurface(surface), request.Audio ? DisplayAudio : null);
    }

    private static MediaCaptureDevice CreateDisplaySurface(string surface) => new()
    {
        Id = "fake-display-" + surface,
        Kind = MediaDeviceKind.VideoInput,
        Label = surface switch
        {
            "monitor" => "screen:0:0",
            "window" => "window:1:0",
            _ => "web-contents-media-stream://1:1",
        },
        Capabilities = new MediaTrackCapabilities
        {
            DisplaySurface = surface,
            LogicalSurface = true,
            Cursor = ["never", "always", "motion"],
            ResizeMode = ["none", "crop-and-scale"],
        },
        DefaultSettings = new MediaTrackSettings
        {
            DisplaySurface = surface,
            LogicalSurface = true,
            Cursor = "always",
            Width = 1920,
            Height = 1080,
            FrameRate = 30,
            ResizeMode = "none",
        },
        VideoModes = [new MediaCaptureVideoMode(1920, 1080, 30)],
    };

    /// <summary>
    /// The camera and the screens: vertical color bars (white, yellow, cyan, green, magenta, red, blue, black) and a white
    /// square that moves one pixel to the right at each frame, on the top of the frame.
    /// </summary>
    private sealed class PatternSource(MediaCaptureRequest request) : MediaCaptureSource(request)
    {
        private static readonly byte[][] BarColors =
        [
            [255, 255, 255], [255, 255, 0], [0, 255, 255], [0, 255, 0], [255, 0, 255], [255, 0, 0], [0, 0, 255], [0, 0, 0],
        ];

        private MediaVideoFrame? _lastFrame;
        private long _lastFrameIndex = -1;

        public override MediaVideoFrame? GetVideoFrame(TimeSpan timestamp)
        {
            var width = (int)(Settings.Width ?? 640);
            var height = (int)(Settings.Height ?? 480);
            var frameRate = Settings.FrameRate is { } rate and > 0 ? rate : 30;
            var frameIndex = (long)Math.Floor(timestamp.TotalSeconds * frameRate);
            if (_lastFrame is { } last && last.Width == width && last.Height == height && _lastFrameIndex == frameIndex)
                return last;

            var pixels = new byte[width * height * 4];
            for (var x = 0; x < width; x++)
            {
                var color = BarColors[x * BarColors.Length / width];
                for (var y = 0; y < height; y++)
                {
                    var offset = ((y * width) + x) * 4;
                    pixels[offset] = color[0];
                    pixels[offset + 1] = color[1];
                    pixels[offset + 2] = color[2];
                    pixels[offset + 3] = 255;
                }
            }

            var size = Math.Max(1, Math.Min(width, height) / 8);
            var left = (int)(frameIndex % Math.Max(1, width - size));
            for (var y = 0; y < size; y++)
            {
                pixels.AsSpan(((y * width) + left) * 4, size * 4).Fill(255);
            }

            _lastFrameIndex = frameIndex;
            _lastFrame = new MediaVideoFrame(width, height, pixels);
            return _lastFrame;
        }
    }

    /// <summary>The microphone: a sine wave of <see cref="ToneFrequency"/> Hz on every channel.</summary>
    private sealed class ToneSource(MediaCaptureRequest request) : MediaCaptureSource(request)
    {
        public override void ReadAudio(long position, Span<float> destination)
        {
            var channels = (int)(Settings.ChannelCount ?? 1);
            var sampleRate = Settings.SampleRate ?? 48000;
            for (var frame = 0; frame < destination.Length / channels; frame++)
            {
                var value = ToneAmplitude * (float)Math.Sin(2 * Math.PI * ToneFrequency * (position + frame) / sampleRate);
                destination.Slice(frame * channels, channels).Fill(value);
            }
        }
    }
}
