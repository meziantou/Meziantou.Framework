"""Handcrafts Monkey's Audio and Musepack SV7 files (a valid header, placeholder audio and an APEv2 tag), and appends an
APEv2 and an ID3v1 tag to plain.mp3 to make ape_only.mp3. Called by generate-test-files.ps1 with the TestFiles directory."""
import os
import struct
import sys

OUT = sys.argv[1]

COVER = open(os.path.join(OUT, "cover.png"), "rb").read()


def ape_item(key, value, flags=0):
    if isinstance(value, str):
        value = value.encode("utf-8")
    return struct.pack("<II", len(value), flags) + key.encode("ascii") + b"\0" + value


def ape_tag(items):
    body = b"".join(items)
    size = len(body) + 32  # items + footer
    def frame(flags):
        return b"APETAGEX" + struct.pack("<IIII", 2000, size, len(items), flags) + b"\0" * 8
    return frame(0xA0000000) + body + frame(0x80000000)


TAG = ape_tag([
    ape_item("Title", "Test Title"),
    ape_item("Artist", "Test Artist"),
    ape_item("Album", "Test Album"),
    ape_item("Album Artist", "Test Album Artist"),
    ape_item("Year", "2024"),
    ape_item("Track", "3/12"),
    ape_item("Disc", "1/2"),
    ape_item("Genre", "Rock"),
    ape_item("Comment", "Test Comment"),
    ape_item("REPLAYGAIN_TRACK_GAIN", "-6.50 dB"),
    ape_item("MUSICBRAINZ_TRACKID", "f1c4a1d2-9d4b-4e0a-8f3e-1c2b3a4d5e6f"),
    ape_item("CUSTOM_FIELD", "Custom Value"),
    ape_item("Cover Art (Front)", b"cover.png\0" + COVER, flags=2),  # binary item
    ape_item("Private Data", b"\x00\x01\x02\x03", flags=2),          # binary item not mapped to a field
])


def monkeys_audio(tag):
    # Monkey's Audio 3.99 layout: APE_DESCRIPTOR (52 bytes) + APE_HEADER (24 bytes) + seek table + WAV header + frames
    wav_header = b"RIFF" + struct.pack("<I", 36 + 88200) + b"WAVEfmt " + struct.pack("<IHHIIHH", 16, 1, 1, 44100, 88200, 2, 16) + b"data" + struct.pack("<I", 88200)
    frames = b"\0" * 64
    seek_table = struct.pack("<I", 52 + 24 + 4 + len(wav_header))
    descriptor = b"MAC " + struct.pack("<HHIIIIIII", 3990, 0, 52, 24, len(seek_table), len(wav_header), len(frames), 0, 0) + b"\0" * 16
    # Compression level 2000, no flags, 73728 blocks per frame, one frame of 66150 blocks: 1.5 s at 44100 Hz
    header = struct.pack("<HHIIIHHI", 2000, 0, 73728, 66150, 1, 16, 1, 44100)
    assert len(descriptor) == 52 and len(header) == 24
    return descriptor + header + seek_table + wav_header + frames + tag


def musepack_sv7(tag):
    # "MP+" + version 7, frame count (LE), then the stream header; byte 10 low 2 bits = sample rate index (0 = 44100 Hz)
    header = b"MP+\x17" + struct.pack("<I", 115) + bytes([0, 0, 0, 0]) + b"\0" * 16
    frames = b"\0" * 64
    return header + frames + tag


open(os.path.join(OUT, "basic.ape"), "wb").write(monkeys_audio(TAG))
open(os.path.join(OUT, "empty.ape"), "wb").write(monkeys_audio(b""))
open(os.path.join(OUT, "sv7.mpc"), "wb").write(musepack_sv7(TAG))

# MP3 with an APEv2 tag followed by an ID3v1 tag, as foobar2000 writes it
MP3_TAG = ape_tag([
    ape_item("Title", "Ape Title"),
    ape_item("Artist", "Ape Artist"),
    ape_item("Album", "Ape Album"),
    ape_item("Track", "7"),
])
id3v1 = b"TAG" + b"Id3v1 Title".ljust(30, b"\0") + b"Id3v1 Artist".ljust(30, b"\0") + b"".ljust(30, b"\0") + b"2001" + b"".ljust(30, b"\0") + bytes([255])
assert len(id3v1) == 128
audio = open(os.path.join(OUT, "plain.mp3"), "rb").read()
open(os.path.join(OUT, "ape_only.mp3"), "wb").write(audio + MP3_TAG + id3v1)
