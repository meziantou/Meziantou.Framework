# Web Audio

The library implements the [Web Audio API](https://webaudio.github.io/web-audio-api/) (namespace
`Meziantou.Framework.WebPlatform.WebAudio`): the audio routing graph, its nodes and the automation of their parameters,
rendered without audio device. The scripts use it through the WebIDL bindings, and the .NET code can use the same
classes directly.

```js
const context = new OfflineAudioContext({ numberOfChannels: 2, length: 44100, sampleRate: 44100 });
const oscillator = new OscillatorNode(context, { type: 'sawtooth', frequency: 220 });
const filter = new BiquadFilterNode(context, { frequency: 1000 });
oscillator.connect(filter).connect(context.destination);
oscillator.start();
const buffer = await context.startRendering();
```

## Rendering

- **Where the graph is rendered**: on the thread of the event loop of the window of the context, never concurrently with
  its scripts. An `OfflineAudioContext` renders from tasks of the event loop, by slices of 16384 frames, so that the other
  tasks (e.g. a call of `close()`, or the events of the nodes) run during a long rendering. A context created by .NET code
  without window renders synchronously: the task returned by `StartRendering()` is completed when it returns.
- **The render quantum**: 128 frames by default, or the size given by `renderSizeHint`. Each render quantum, the nodes are
  processed in dependency order. The order is computed again when the connections change: the nodes without connection are
  skipped (except the started sources, so that they fire `ended`), a `DelayNode` in a cycle is split (its output is read
  from its delay line before the other nodes, which clamps its delay to one render quantum, and its input is written after
  them), and the nodes of a cycle without `DelayNode` are muted.
- **Actively processing**: a node whose inputs are not actively processing outputs a single channel of silence once its
  tail time has elapsed (the delay of a `DelayNode`, the length of the response of a `ConvolverNode`, until the state of a
  filter decays to silence...).
- **Channels**: the inputs are up-mixed and down-mixed with the speaker layouts (mono, stereo, quad, 5.1) or discretely
  ([channel mixing](https://webaudio.github.io/web-audio-api/#channel-up-mixing-and-down-mixing)).
- **Automation**: the events of an `AudioParam` take effect at the first frame at or after their time
  (`ceil(time × sampleRate)`), and the formulas of the specification are computed in double precision at the exact time of
  the frames; as in the browsers, the event values are single-precision floats and a linear ramp adds its change to its
  start value in single precision (an automation and the same automation from 0 added by an audio-rate input give the same
  values). The values are clamped to the nominal range after the automation.
- **Sources**: the start and stop times are sub-sample accurate (the first frame of an `AudioBufferSourceNode` or an
  `OscillatorNode` accounts for the time elapsed since the start time); a time within 10⁻⁶ frame of a frame is that frame.
  The `AudioBuffer` samples are linearly interpolated; after the last frame of a buffer that does not loop, the signal is
  extrapolated from its last two frames.

## Nodes

| Node | Notes |
|---|---|
| `AudioBufferSourceNode` | Loops with sub-sample loop points, playback rate and detune (k-rate), negative rates, `duration` |
| `ConstantSourceNode`, `GainNode` | |
| `OscillatorNode`, `PeriodicWave` | The waveforms are band-limited to the Nyquist frequency: summed directly up to 64 partials, else interpolated from wavetables of 4096 points (one per third of an octave); the normalization of the specification |
| `DelayNode` | Linear interpolation; the output has the channels of the delayed input |
| `BiquadFilterNode` | The coefficients of the Audio EQ Cookbook with the edge cases of the browsers, computed per frame when a parameter is automated; `getFrequencyResponse()` |
| `IIRFilterNode` | Up to 20 coefficients; `getFrequencyResponse()` |
| `ConvolverNode` | Responses of 1, 2 or 4 channels (true stereo); direct convolution up to 64 frames, else a uniformly partitioned overlap-save FFT convolution; the normalization of the specification |
| `WaveShaperNode` | The `2x` and `4x` oversampling use windowed-sinc filters (latency of 32 frames) |
| `DynamicsCompressorNode` | The structure of the specification (look-ahead of 6 ms, envelope follower, soft knee, makeup gain); the curves are not the ones of a browser |
| `StereoPannerNode`, `PannerNode`, `AudioListener` | Equal-power panning, distance models and cones of the specification. There is no measured HRTF database: the `HRTF` panning model is a structural model of the head (interaural delay, head shadow and pinna echoes, C. P. Brown and R. O. Duda, 1998) |
| `ChannelSplitterNode`, `ChannelMergerNode` | |
| `AnalyserNode` | The last 32768 frames (down-mixed to mono), Blackman window, smoothing over time |
| `ScriptProcessorNode` | The `audioprocess` events are fired during the rendering, and their output is played two buffers later |

## Not implemented

- `AudioWorklet`, `AudioWorkletNode`, `AudioWorkletProcessor`.
- `decodeAudioData()`.
- `AudioContext` (the realtime context), `MediaElementAudioSourceNode`, the `MediaStream` nodes.

The results of the web-platform-tests are in [docs/wpt-coverage/webaudio.md](wpt-coverage/webaudio.md).
