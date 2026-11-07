#!/bin/sh
# Writes the gain map fixtures of the JPEG and AVIF tests. Run from the root of the repository:
#   docker build -t mfw-gain-map-fixtures tools/Meziantou.Framework.Imaging.GainMapFixtures
#   docker run --rm -v "$PWD:/repo" mfw-gain-map-fixtures sh /repo/tools/Meziantou.Framework.Imaging.GainMapFixtures/generate.sh
set -eu
tools=/repo/tools/Meziantou.Framework.Imaging.GainMapFixtures
jpeg=/repo/tests/Meziantou.Framework.Imaging.Jpeg.Tests/GainMaps
avif=/repo/tests/Meziantou.Framework.Imaging.Avif.Tests/GainMaps
work=$(mktemp -d)
cd "$work"

# A synthetic scene of 64x48 linear-light pixels (half floats), up to about 10 times the white of standard dynamic range
python3 "$tools/hdr_scene.py" 64 48 hdr.rgbahalf

# Ultra HDR: libultrahdr tone maps the scene to an SDR base image (Display P3) and stores the gain map (ISO 21496-1
# metadata), then decodes the file to linear half floats (in the gamut of the HDR intent, Display P3 here)
ultrahdr_app -m 0 -p hdr.rgbahalf -a 4 -t 0 -C 1 -w 64 -h 48 -q 95 -Q 95 -M 1 -z "$jpeg/ultrahdr-multichannel.jpg"
ultrahdr_app -m 0 -p hdr.rgbahalf -a 4 -t 0 -C 1 -w 64 -h 48 -q 95 -Q 95 -M 0 -s 2 -z "$jpeg/ultrahdr-downscaled.jpg"
ultrahdr_app -m 1 -j "$jpeg/ultrahdr-multichannel.jpg" -o 0 -O 4 -z "$jpeg/ultrahdr-multichannel.linear.f16"
ultrahdr_app -m 1 -j "$jpeg/ultrahdr-downscaled.jpg" -o 0 -O 4 -z "$jpeg/ultrahdr-downscaled.linear.f16"

# AVIF: an SDR base image (sRGB) and the HDR scene (PQ, BT.709 or BT.2020 primaries) combined by libavif
python3 "$tools/sdr_hdr_pair.py" 64 48
convert hdr709.ppm -depth 16 hdr709.png
convert hdr2020.ppm -depth 16 hdr2020.png
avifgainmaputil combine sdr.png hdr709.png "$avif/gainmap-lossless.avif" --cicp-base 1/13/6 --cicp-alternate 1/16/9 -q 100 --qgain-map 100
avifgainmaputil combine sdr.png hdr709.png "$avif/gainmap-downscaled.avif" --cicp-base 1/13/6 --cicp-alternate 1/16/9 -q 90 --qgain-map 90 --yuv-gain-map 400 --downscaling 2
avifgainmaputil combine sdr.png hdr2020.png "$avif/gainmap-bt2020-alternate.avif" --cicp-base 1/13/6 --cicp-alternate 9/16/9 -q 100 --qgain-map 100

# The renditions of the AVIF files at full weight: ISO 21496-1 evaluated with numpy on the planes libavif decodes
cd "$avif"
python3 "$tools/avif_reference.py" gainmap-lossless.avif
python3 "$tools/avif_reference.py" gainmap-downscaled.avif
python3 "$tools/avif_reference.py" gainmap-bt2020-alternate.avif bt2020
