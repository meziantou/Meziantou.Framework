#!/usr/bin/env pwsh

# Generates test fixture files using ffmpeg.
# Run once: pwsh ./generate-test-files.ps1
# Requires: ffmpeg with libmp3lame, libvorbis, libopus, aac encoders.

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

Write-Host "Done! Generated test files in $testFilesDirectory"
Get-ChildItem -Path $testFilesDirectory -Force | Sort-Object Name
