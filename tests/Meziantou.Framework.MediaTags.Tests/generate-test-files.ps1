#!/usr/bin/env pwsh

# Generates test fixture files using ffmpeg.
# Run once: pwsh ./generate-test-files.ps1
# Requires: ffmpeg with libmp3lame, libvorbis, libopus, aac, wmav2, wmv2, wavpack and mpeg4 encoders, mpcenc
# (musepack-tools) and python3. Debian 13 provides all of them (apt-get install ffmpeg musepack-tools python3).

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Ffmpeg {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & ffmpeg @Arguments 2> $null
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed with exit code $($LASTEXITCODE): ffmpeg $($Arguments -join ' ')"
    }

    # ffmpeg has exited 0 while leaving an empty file behind, and an empty fixture that no test reads is
    # invisible until someone writes a test against it. The output is checked here instead.
    $outputPath = $Arguments[-1]
    if (-not (Test-Path -LiteralPath $outputPath)) {
        throw "ffmpeg reported success but produced no file: $outputPath"
    }

    if ((Get-Item -LiteralPath $outputPath).Length -eq 0) {
        throw "ffmpeg reported success but produced an empty file: $outputPath"
    }
}

Get-Command ffmpeg -ErrorAction Stop | Out-Null

$scriptDirectory = Split-Path -Parent $PSCommandPath
$testFilesDirectory = Join-Path $scriptDirectory "TestFiles"
New-Item -ItemType Directory -Path $testFilesDirectory -Force | Out-Null

Write-Host "Generating test files in $testFilesDirectory..."

# Create a small 1x1 red PNG for cover art tests.
$coverPngBytes = [Convert]::FromHexString("89504E470D0A1A0A0000000D4948445200000001000000010802000000907753DE0000000C49444154789C63F80F00000101000518D84E0000000049454E44AE426082")
[System.IO.File]::WriteAllBytes((Join-Path $testFilesDirectory "cover.png"), $coverPngBytes)

# ================================
# BASIC FILES - core metadata
# ================================

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Test Title", "-metadata", "artist=Test Artist",
    "-metadata", "album=Test Album", "-metadata", "track=3",
    "-metadata", "date=2024", "-metadata", "genre=Rock",
    "-c:a", "libmp3lame", "-q:a", "9", (Join-Path $testFilesDirectory "basic.mp3")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Test Title", "-metadata", "artist=Test Artist",
    "-metadata", "album=Test Album", "-metadata", "track=3",
    "-metadata", "date=2024", "-metadata", "genre=Rock",
    "-c:a", "libvorbis", "-q:a", "0", (Join-Path $testFilesDirectory "basic.ogg")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Test Title", "-metadata", "artist=Test Artist",
    "-metadata", "album=Test Album", "-metadata", "track=3",
    "-metadata", "date=2024", "-metadata", "genre=Rock",
    "-c:a", "libopus", "-b:a", "32k", (Join-Path $testFilesDirectory "basic.opus")

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-c:a", "libopus", "-b:a", "32k", (Join-Path $testFilesDirectory "empty.opus")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Ünïcödé Títlé", "-metadata", "artist=Àrtïst 日本語",
    "-c:a", "libopus", "-b:a", "32k", (Join-Path $testFilesDirectory "unicode.opus")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=All Fields Title", "-metadata", "artist=All Fields Artist",
    "-metadata", "album=All Fields Album", "-metadata", "ALBUMARTIST=All Fields Album Artist",
    "-metadata", "genre=Electronic", "-metadata", "date=2023", "-metadata", "TRACKNUMBER=5",
    "-metadata", "TRACKTOTAL=12", "-metadata", "DISCNUMBER=2", "-metadata", "DISCTOTAL=3",
    "-metadata", "composer=All Fields Composer", "-metadata", "comment=All Fields Comment",
    "-metadata", "copyright=2023 Test",
    "-c:a", "libopus", "-b:a", "32k", (Join-Path $testFilesDirectory "all_fields.opus")
)
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Test Title", "-metadata", "artist=Test Artist",
    "-metadata", "album=Test Album", "-metadata", "TRACKNUMBER=3",
    "-metadata", "date=2024", "-metadata", "genre=Rock",
    (Join-Path $testFilesDirectory "basic.flac")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Test Title", "-metadata", "artist=Test Artist",
    "-metadata", "album=Test Album", "-metadata", "track=3",
    "-metadata", "date=2024", "-metadata", "genre=Rock",
    "-c:a", "aac", "-b:a", "64k", (Join-Path $testFilesDirectory "basic.m4a")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Test Title", "-metadata", "artist=Test Artist",
    "-metadata", "album=Test Album",
    (Join-Path $testFilesDirectory "basic.wav")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Test Title", "-metadata", "artist=Test Artist",
    "-metadata", "album=Test Album",
    (Join-Path $testFilesDirectory "basic.aiff")
)

# ================================
# UNICODE FILES
# ================================

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=日本語テスト", "-metadata", "artist=Тест Артист",
    "-metadata", "album=Tëst Àlbüm",
    "-c:a", "libmp3lame", "-q:a", "9", (Join-Path $testFilesDirectory "unicode.mp3")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=日本語テスト", "-metadata", "artist=Тест Артист",
    "-metadata", "album=Tëst Àlbüm",
    "-c:a", "libvorbis", "-q:a", "0", (Join-Path $testFilesDirectory "unicode.ogg")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=日本語テスト", "-metadata", "artist=Тест Артист",
    "-metadata", "album=Tëst Àlbüm",
    (Join-Path $testFilesDirectory "unicode.flac")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=日本語テスト", "-metadata", "artist=Тест Артист",
    "-metadata", "album=Tëst Àlbüm",
    "-c:a", "aac", "-b:a", "64k", (Join-Path $testFilesDirectory "unicode.m4a")
)

# ================================
# EMPTY FILES (no metadata)
# ================================

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-c:a", "libmp3lame", "-q:a", "9", "-write_id3v1", "0", "-write_id3v2", "0",
    (Join-Path $testFilesDirectory "empty.mp3")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-c:a", "libvorbis", "-q:a", "0", (Join-Path $testFilesDirectory "empty.ogg")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    (Join-Path $testFilesDirectory "empty.flac")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-c:a", "aac", "-b:a", "64k", (Join-Path $testFilesDirectory "empty.m4a")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    (Join-Path $testFilesDirectory "empty.wav")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    (Join-Path $testFilesDirectory "empty.aiff")
)

# ================================
# ALL FIELDS FILES
# ================================

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=All Fields Title", "-metadata", "artist=All Fields Artist",
    "-metadata", "album=All Fields Album", "-metadata", "album_artist=All Fields Album Artist",
    "-metadata", "genre=Electronic", "-metadata", "date=2023", "-metadata", "track=5/12",
    "-metadata", "disc=2/3", "-metadata", "composer=All Fields Composer",
    "-metadata", "comment=All Fields Comment", "-metadata", "copyright=2023 Test",
    "-c:a", "libmp3lame", "-q:a", "9", (Join-Path $testFilesDirectory "all_fields.mp3")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=All Fields Title", "-metadata", "artist=All Fields Artist",
    "-metadata", "album=All Fields Album", "-metadata", "ALBUMARTIST=All Fields Album Artist",
    "-metadata", "genre=Electronic", "-metadata", "date=2023", "-metadata", "TRACKNUMBER=5",
    "-metadata", "TRACKTOTAL=12", "-metadata", "DISCNUMBER=2", "-metadata", "DISCTOTAL=3",
    "-metadata", "composer=All Fields Composer", "-metadata", "comment=All Fields Comment",
    "-metadata", "copyright=2023 Test",
    "-c:a", "libvorbis", "-q:a", "0", (Join-Path $testFilesDirectory "all_fields.ogg")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=All Fields Title", "-metadata", "artist=All Fields Artist",
    "-metadata", "album=All Fields Album", "-metadata", "ALBUMARTIST=All Fields Album Artist",
    "-metadata", "genre=Electronic", "-metadata", "date=2023", "-metadata", "TRACKNUMBER=5",
    "-metadata", "TRACKTOTAL=12", "-metadata", "DISCNUMBER=2", "-metadata", "DISCTOTAL=3",
    "-metadata", "composer=All Fields Composer", "-metadata", "comment=All Fields Comment",
    "-metadata", "copyright=2023 Test",
    (Join-Path $testFilesDirectory "all_fields.flac")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=All Fields Title", "-metadata", "artist=All Fields Artist",
    "-metadata", "album=All Fields Album", "-metadata", "album_artist=All Fields Album Artist",
    "-metadata", "genre=Electronic", "-metadata", "date=2023", "-metadata", "track=5/12",
    "-metadata", "disc=2/3", "-metadata", "composer=All Fields Composer",
    "-metadata", "comment=All Fields Comment", "-metadata", "copyright=2023 Test",
    "-c:a", "aac", "-b:a", "64k", (Join-Path $testFilesDirectory "all_fields.m4a")
)

# ================================
# WITH ART FILES
# ================================

$coverPath = Join-Path $testFilesDirectory "cover.png"

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-i", $coverPath,
    "-metadata", "title=Art Title",
    "-c:a", "libmp3lame", "-q:a", "9",
    "-map", "0:a", "-map", "1:v", "-c:v", "copy", "-disposition:v:0", "attached_pic",
    "-id3v2_version", "3",
    (Join-Path $testFilesDirectory "with_art.mp3")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-i", $coverPath,
    "-metadata", "title=Art Title",
    "-map", "0:a", "-map", "1:v", "-c:v", "copy", "-disposition:v:0", "attached_pic",
    (Join-Path $testFilesDirectory "with_art.flac")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-i", $coverPath,
    "-metadata", "title=Art Title",
    "-c:a", "aac", "-b:a", "64k",
    "-map", "0:a", "-map", "1:v", "-c:v", "copy", "-disposition:v:0", "attached_pic",
    (Join-Path $testFilesDirectory "with_art.m4a")
)

# ================================
# LONG VALUES FILES
# ================================

$longTitle = "A" * 4096
$longComment = "B" * 4096

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=$longTitle", "-metadata", "comment=$longComment",
    "-c:a", "libmp3lame", "-q:a", "9", (Join-Path $testFilesDirectory "long_values.mp3")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=$longTitle", "-metadata", "comment=$longComment",
    (Join-Path $testFilesDirectory "long_values.flac")
)

# ================================
# ID3v1 ONLY (MP3 without ID3v2)
# ================================

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=ID3v1 Title", "-metadata", "artist=ID3v1 Artist",
    "-metadata", "album=ID3v1 Album", "-metadata", "date=2024", "-metadata", "genre=Pop",
    "-metadata", "track=7",
    "-c:a", "libmp3lame", "-q:a", "9", "-write_id3v2", "0",
    (Join-Path $testFilesDirectory "id3v1_only.mp3")
)

# ================================
# ASF (WMA / WMV)
# ================================

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Test Title", "-metadata", "artist=Test Artist", "-metadata", "album=Test Album",
    "-metadata", "track=3", "-metadata", "date=2024", "-metadata", "genre=Rock",
    "-c:a", "wmav2", "-b:a", "32k", (Join-Path $testFilesDirectory "basic.wma")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=All Fields Title", "-metadata", "artist=All Fields Artist", "-metadata", "album=All Fields Album",
    "-metadata", "album_artist=All Fields Album Artist", "-metadata", "genre=Electronic", "-metadata", "date=2023",
    "-metadata", "track=5", "-metadata", "disc=1/2", "-metadata", "composer=Test Composer",
    "-metadata", "copyright=2023 Test", "-metadata", "comment=Test Comment",
    "-metadata", "WM/Conductor=Test Conductor", "-metadata", "WM/ISRC=USRC17607839", "-metadata", "WM/BeatsPerMinute=128",
    "-metadata", "WM/Lyrics=Line 1", "-metadata", "REPLAYGAIN_TRACK_GAIN=-6.50 dB",
    "-metadata", "MusicBrainz/Track Id=f1c4a1d2-9d4b-4e0a-8f3e-1c2b3a4d5e6f", "-metadata", "CUSTOM_FIELD=Custom Value",
    "-c:a", "wmav2", "-b:a", "32k", (Join-Path $testFilesDirectory "all_fields.wma")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=日本語テスト", "-metadata", "artist=Тест Артист", "-metadata", "album=Tëst Àlbüm",
    "-c:a", "wmav2", "-b:a", "32k", (Join-Path $testFilesDirectory "unicode.wma")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-fflags", "+bitexact", "-map_metadata", "-1", "-c:a", "wmav2", "-b:a", "32k", (Join-Path $testFilesDirectory "empty.wma")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=10:duration=1", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Video Title", "-metadata", "artist=Video Artist",
    "-c:v", "wmv2", "-c:a", "wmav2", "-b:a", "32k", "-shortest", (Join-Path $testFilesDirectory "basic.wmv")
)

# ================================
# APE TAGS (WavPack, Musepack, MP3)
# ================================

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=Test Title", "-metadata", "artist=Test Artist", "-metadata", "album=Test Album",
    "-metadata", "track=3", "-metadata", "date=2024", "-metadata", "genre=Rock",
    "-c:a", "wavpack", (Join-Path $testFilesDirectory "basic.wv")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-fflags", "+bitexact", "-map_metadata", "-1", "-c:a", "wavpack", (Join-Path $testFilesDirectory "empty.wv")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-metadata", "title=日本語テスト", "-metadata", "artist=Тест Артист", "-metadata", "album=Tëst Àlbüm",
    "-c:a", "wavpack", (Join-Path $testFilesDirectory "unicode.wv")
)

# mpcenc encodes Musepack stream version 8 and writes an APEv2 tag
$sineWav = Join-Path ([System.IO.Path]::GetTempPath()) "mediatags-sine.wav"
Invoke-Ffmpeg -Arguments @("-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-ar", "44100", "-ac", "2", $sineWav)
& mpcenc --silent --overwrite --title "Test Title" --artist "Test Artist" --album "Test Album" --track 3 --year 2024 --genre Rock $sineWav (Join-Path $testFilesDirectory "basic.mpc")
& mpcenc --silent --overwrite $sineWav (Join-Path $testFilesDirectory "empty.mpc")
Remove-Item -LiteralPath $sineWav

# The MP3 audio of ape_only.mp3, which generate-handcrafted-test-files.py tags
$plainMp3 = Join-Path $testFilesDirectory "plain.mp3"
Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-c:a", "libmp3lame", "-q:a", "9", "-id3v2_version", "0", "-write_id3v1", "0", $plainMp3
)

# Monkey's Audio and Musepack stream version 7 have no encoder in ffmpeg: their headers are written by hand, with an
# APEv2 tag written independently of the library.
& python3 (Join-Path $scriptDirectory "generate-handcrafted-test-files.py") $testFilesDirectory
if ($LASTEXITCODE -ne 0) {
    throw "generate-handcrafted-test-files.py failed with exit code $LASTEXITCODE"
}

Remove-Item -LiteralPath $plainMp3

# ================================
# MP4 VIDEO
# ================================

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=10:duration=1", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-c:v", "mpeg4", "-c:a", "aac", "-b:a", "32k", "-shortest",
    "-metadata", "title=Episode Title", "-metadata", "artist=Video Artist", "-metadata", "show=Test Show",
    "-metadata", "network=Test Network", "-metadata", "episode_id=S01E02", "-metadata", "season_number=1",
    "-metadata", "episode_sort=2", "-metadata", "media_type=10", "-metadata", "description=Short description",
    "-metadata", "synopsis=Long description", "-metadata", "hd_video=1", "-f", "mp4", (Join-Path $testFilesDirectory "tv_show.m4v")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=10:duration=2",
    "-c:v", "mpeg4", "-metadata", "title=Video Only", "-f", "mp4", (Join-Path $testFilesDirectory "video_only.mp4")
)

Invoke-Ffmpeg -Arguments @(
    "-y", "-f", "lavfi", "-i", "testsrc=size=32x16:rate=10:duration=1", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
    "-c:v", "mpeg4", "-c:a", "aac", "-b:a", "32k", "-shortest", "-metadata", "title=QuickTime Title",
    "-f", "mov", (Join-Path $testFilesDirectory "basic.mov")
)

$emptyFiles = Get-ChildItem -Path $testFilesDirectory -File -Force | Where-Object { $_.Length -eq 0 }
if ($emptyFiles) {
    throw "These fixtures are empty: $($emptyFiles.Name -join ', ')"
}

Write-Host "Done! Generated test files in $testFilesDirectory"
Get-ChildItem -Path $testFilesDirectory -Force | Sort-Object Name
