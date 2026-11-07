namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Small HEIC files written by <c>heif-enc</c> (libheif 1.23.6 with x265 4.2, from the meziantou/prebuilt release 3.5.0),
/// embedded so that the decoding tests need no tool. Every file is lossless (<c>-L</c>: identity matrix, 4:4:4, full
/// range), so the expected pixels are the ones of its source, which <see cref="GetSourceSample"/> and
/// <see cref="GetBlockSample"/> compute. The sources are PNG files of the same samples. libheif codes an image that is
/// smaller than 64x64 as a picture of 64x64 with a clean aperture: the 8x6 images have one.
/// </summary>
/// <remarks>
/// <code>
/// heif-enc -L rgb.png -o rgb.heic                 (8x6, 8-bit RGB)
/// heif-enc -L rgba.png -o rgba.heic               (8x6, 8-bit RGBA: an alpha auxiliary image)
/// heif-enc -L gray.png -o gray.heic               (8x6, 8-bit gray)
/// heif-enc -L -b 10 deep.png -o deep.heic         (8x6, 16-bit RGB source whose samples are 10-bit values scaled to 16 bits)
/// heif-enc -L --cut-tiles 64 grid.png -o grid.heic    (130x70: a grid of 3x2 tiles of 64x64)
/// heif-enc -L -S --timebase 1000 --duration 40 --gop-structure intra-only --repetitions 3 seq-00.png -o seq.heics    (3 images of 72x66)
/// heif-enc -L -S --timebase 600 --duration 25 --gop-structure unrestricted --repetitions infinite s-00.png -o reordered.heics    (4 images of 64x64)
/// </code>
/// </remarks>
internal static class HeicTestFiles
{
    /// <summary>Gets the pixel (x, y) of the source of the 8x6 images.</summary>
    public static (int R, int G, int B, int A) GetSourceSample(int x, int y)
    {
        return (((16 * x) + (3 * y) + 3) & 255, ((32 * y) + (5 * x) + 5) & 255, (255 - (12 * x) - (8 * y)) & 255, (x + y) % 5 is 0 ? 0 : (255 - (20 * y)) & 255);
    }

    /// <summary>
    /// Gets the pixel (x, y) of the block pattern, the source of <see cref="Grid"/> and of the image <paramref name="frame"/>
    /// of <see cref="Sequence"/>: blocks of 8x8 pixels of one color.
    /// </summary>
    public static (int R, int G, int B) GetBlockSample(int x, int y, int frame = 0)
    {
        var blockX = (x + (8 * frame)) >> 3;
        var blockY = (y + (16 * frame)) >> 3;
        return (((37 * blockX) + (11 * blockY) + 20) & 255, ((53 * blockY) + (7 * blockX) + 40) & 255, (200 - (29 * blockX) - (13 * blockY)) & 255);
    }

    /// <summary>Gets the 10-bit sample of the source of <see cref="Deep"/> for an 8-bit sample of <see cref="GetSourceSample"/> in column <paramref name="x"/>.</summary>
    public static int ToTenBits(int sample, int x) => ((sample * 4) + (x & 3)) & 1023;

    /// <summary>An 8x6 RGB image.</summary>
    public static byte[] Rgb => Convert.FromBase64String(
        "AAAAHGZ0eXBoZWl4AAAAAG1pZjFoZWl4bWlhZgAAAX5tZXRhAAAAAAAAACFoZGxyAAAAAAAAAABwaWN0AAAAAAAAAAAAAAAAAAAAACJpbG9jAAAAAERAAAEAAQAAAAABogABAAAAAAAA" +
        "AzAAAAAjaWluZgAAAAAAAQAAABVpbmZlAgAAAAABAABodmMxAAAAAA5waXRtAAAAAAABAAAA/mlwcnAAAADeaXBjbwAAAHdodmNDAQQIAAAAnjgAAAAA//AA/P/4+AAADwNgAAEAF0AB" +
        "DAH//wQIAAADAJ44AAADAAD/ugJAYQABACpCAQEECAAAAwCeOAAAAwAA/5AEECCy3VXTXNwENAAQAAADAZAAAAMAEIBiAAEACEQBwXAwYLEgAAAAE2NvbHJuY2x4AAEADQAAgAAAABRp" +
        "c3BlAAAAAAAAAEAAAABAAAAAKGNsYXAAAAAIAAAAAQAAAAYAAAAB////yAAAAAL////GAAAAAgAAABBwaXhpAAAAAAMICAgAAAAYaXBtYQAAAAAAAAABAAEFgQIDBYQAAAM4bWRhdAAA" +
        "AywoAa8FsgqJRXFKH5con8Y7//+OTWa/ifAiDZWKdrYmr1sfnK1sn//8iauAL2eBSuUriJ0MzxPFr4zRixiySTBzxUg12k//ZBz//9//A/xX2PV+C8v2Phvk/c+J+W+M+Z+cFSprrr2B" +
        "6exsQmNfnv//5ZcooYTQFChQUeed8IQY98Z+gDzdruXMcxK1atWOCCBCEHzAAAHK0hZW1v9Bh4eHiG5ublf33zmZZ2f9sfczcu96My+DL23/+4SMSqp0kQSJAlIAa6aFO0/7oCi2k0WT" +
        "//c3OMVrWty97EQ+50IAHZmZmZu7skjyUEcP//HYAlb0YxF73fkSiHSz/2kq2q82xcmqEyzuE/R1xM+9nOHJj7qN0AYBwQF/doLk5wEAaF9JYBP4+WE4d0gl0ro7+oadjFmrQAADuIz9" +
        "5R9soflemkiI//7tZP3PZzlBCC7EYpvo/9THmhdqfEooc0oYbOjLbS/+tN5aikzVw4f/+OwBK3oxiL3u/Ik3id/6wQnkO8P0mGMz4O2n8e6MdBGbZsxT2sMGhAL//ij2HAKYxT73nNV7" +
        "yT/91se0XuydTtKMpVth8+C/LVwwnx65Zi//99tnSvBCDFKUEqtKZv/zGLdHjCCnk96KHvRVqFy+p4iWHOZzXIItL//hjtDs1OcqIQYeaG4//9fpXrGLlKyrFvQHtL1GIFU5olrY+vRH" +
        "//XqTrF8QgtSk++8X6V/60RLxjljsU1I222s9l/zmfHziqUOiSdtJB/0vLlqtV90AALAp0AEP6PEJEVfm+8YVn8gABAag8SHe2kFskEGYQAGpA7/W3r9Tq/fBgDKMPkHnZsZmDYEuxft" +
        "Z/CM4AAIwNYrT3X9c2ZYidtKQIArggSjyc5ebXQAAL/HZw02MhFQp09HpPk0EAABlQd9yyEIrgO3A5FQT4q2frz+n2QgrRAJ/HywnDukEupcmTJPiF13oAAGNOLJFDeQeBKy8qR5gAi3" +
        "2nuPLjSaIAAJ8VcsvGCPZmHyhHiTtmEAAFJ4RbgqMjEBDxjF8Ixiqyow55lQQM1gCppLf3Z4Nhpnx/KjdzDOZXAAEWJxKIafaKBBR4fHEA==");

    /// <summary>An 8x6 RGB image with an alpha plane.</summary>
    public static byte[] Rgba => Convert.FromBase64String(
        "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");

    /// <summary>An 8x6 gray image: the sample of a pixel is <c>(R + G) &amp; 255</c> of the source.</summary>
    public static byte[] Gray => Convert.FromBase64String(
        "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" +
        "s7ay66iUEJp18jeA");

    /// <summary>An 8x6 RGB image of 10 bits per sample.</summary>
    public static byte[] Deep => Convert.FromBase64String(
        "AAAAHGZ0eXBoZWl4AAAAAG1pZjFoZWl4bWlhZgAAAX5tZXRhAAAAAAAAACFoZGxyAAAAAAAAAABwaWN0AAAAAAAAAAAAAAAAAAAAACJpbG9jAAAAAERAAAEAAQAAAAABogABAAAAAAAA" +
        "AWkAAAAjaWluZgAAAAAAAQAAABVpbmZlAgAAAAABAABodmMxAAAAAA5waXRtAAAAAAABAAAA/mlwcnAAAADeaXBjbwAAAHdodmNDAQQIAAAAnDgAAAAA//AA/P/6+gAADwNgAAEAF0AB" +
        "DAH//wQIAAADAJw4AAADAAD/ugJAYQABACpCAQEECAAAAwCcOAAAAwAA/5AEECCbLdVdNc3AQ0ABAAADABkAAAMAAQhiAAEACEQBwXAwYLEgAAAAE2NvbHJuY2x4AAEADQAAgAAAABRp" +
        "c3BlAAAAAAAAAEAAAABAAAAAKGNsYXAAAAAIAAAAAQAAAAYAAAAB////yAAAAAL////GAAAAAgAAABBwaXhpAAAAAAMKCgoAAAAYaXBtYQAAAAAAAAABAAEFgQIDBYQAAAFxbWRhdAAA" +
        "AWUoAa8FsgmigcDQH5crX0Iln//yP+T/sf4b/IPV/ov+IPzb4v+of42/S/62/kv+yyof//2ErgC96MKdXJtnG7rKI5isUTUJvcIpuDJwuylD4a+oOGDBGUmrz//kkv//+0Wne/Tjk3Pu" +
        "S4u7kkVYzx8zwD2Gx01RE7WOP1TJG1K/eLUuon+wPxjkgCaLu4mNfnv//+cd8jmx+KLs3XPvvvvvf/uq6roTOTIz74z9AHnwRfeZt1ytxRRKRbkW5FtFI9g9g9a5/PzhAAAEx0pPosOf" +
        "xjX6uzqgaoGqBtMjJkyZMmBz5gxgxQll+3//cwMcJzJJo5SA+75rok09t//8L6MmDrd0bbT9Pd07nIffH7Uyk2TrT/S9sa30HgwrC12rXO//+nHSUNXG3d3d3eBYWFffffYL2GAD+P8X" +
        "ttttttvvvvOc5scP8KCj03NMIPVXqdBj88UL8NEdQHF+ntxITMB3eryf9rA=");

    /// <summary>A 130x70 RGB image of the block pattern, in a grid of 3x2 tiles of 64x64: the tiles of the last row and column exceed the image.</summary>
    public static byte[] Grid => Convert.FromBase64String(
        "AAAAHGZ0eXBoZWl4AAAAAG1pZjFoZWl4bWlhZgAAAr5tZXRhAAAAAAAAACFoZGxyAAAAAAAAAABwaWN0AAAAAAAAAAAAAAAAAAAAABBpZGF0AAABAgCCAEYAAACcaWxvYwEAAABEQAAH" +
        "AAEAAQAAAAAAAAABAAAAAAAAAAgAAgAAAAAAAALiAAEAAAAAAAAMowADAAAAAAAAD4UAAQAAAAAAAAzQAAQAAAAAAAAcVQABAAAAAAAAAX4ABQAAAAAAAB3TAAEAAAAAAAADHQAGAAAA" +
        "AAAAIPAAAQAAAAAAAALtAAcAAAAAAAAj3QABAAAAAAAAAIMAAAChaWluZgAAAAAABwAAABVpbmZlAgAAAAABAABncmlkAAAAABVpbmZlAgAAAQACAABodmMxAAAAABVpbmZlAgAAAQAD" +
        "AABodmMxAAAAABVpbmZlAgAAAQAEAABodmMxAAAAABVpbmZlAgAAAQAFAABodmMxAAAAABVpbmZlAgAAAQAGAABodmMxAAAAABVpbmZlAgAAAQAHAABodmMxAAAAAA5waXRtAAAAAAAB" +
        "AAABEmlwcnAAAADKaXBjbwAAABRpc3BlAAAAAAAAAIIAAABGAAAAd2h2Y0MBBAgAAACeOAAAAAD/8AD8//j4AAAPA2AAAQAXQAEMAf//BAgAAAMAnjgAAAMAAP+6AkBhAAEAKkIBAQQI" +
        "AAADAJ44AAADAAD/kAQQILLdSSU1zcBDQAEAAAMAGQAAAwABCGIAAQAIRAHBcDBgkSAAAAATY29scm5jbHgAAQANAACAAAAAFGlzcGUAAAAAAAAAQAAAAEAAAAAQcGl4aQAAAAADCAgI" +
        "AAAAQGlwbWEAAAAAAAAABwABAwGFAwACBIIDBAUAAwSCAwQFAAQEggMEBQAFBIIDBAUABgSCAwQFAAcEggMEBQAAACRpcmVmAAAAAAAAABhkaW1nAAEABgACAAMABAAFAAYABwAAIYZt" +
        "ZGF0AAAMnygBrwW4FHuiWf//k/8l/HfhvW9b1vW9f1/X9f1/X9f1yof//ImrgC9o/dGcO/GHadp2naeZZlmWZZlmWZXXP/2Qc///p/9N/Rfmvivivivivi/i/i/i/i/i/i/i7wJmZ7cI" +
        "Bi4ZmZm1VVVUVN/axX//v0eNh9W2225ttttttr7f5oKAPMPyF2cUaNGjRwgQIECBAeLzhgAOgaFLIUbr6EAAQFCgfOXINrq6urrK6urq6urq3prTx//+BKi+9734MYxjGBhVfYAvyeQh" +
        "CEIdGMYxixwAAaT/M5o9NyMO70qrbYrDuvwxHKAALOymZmcv///4KbS//+Hvc5znOlKUpSlAHWMAH15znOc51rWta1pG14AA8yeDZTozprAAFfygAABiqqqm2Pnf/9TaXlqOeeee8888" +
        "888oT2ABxj8Ru4jbt27dvw4cOHDhwrfq5wACMtzMzNaqqqpKmS//6FvqGRWIIIJK666666O5eAKZblsZdWXLly5h+/fv379903GyGdgAGco5mZoNVVVQ2H//9kCie9736MYxjF/GOANe" +
        "pWta1raQhCEIMkMAAFKMUsoAW41+dAAUzGAAAF1VVVG2Z//n7L+UpSm0IQhCCEAAFHada1rWuxCEIQfj4AACpPa8XLwuMYACgCh48vsKUGBgYGEBgYGBgYGA8zv/+1xyVrWtf73ve91g" +
        "AAfXnOc5znWta1rWgAACBk2B8dAxxIUahjE9M0qScdzSAAhRwAAAJVVVU+r//9apPve98DGMYxi4AACQl1znOc6ZrWta1CgwABdQVYHjNvJYACRlMSTxKdt/f39/iKioqKioqKDJf/7r" +
        "x/YxjGfWta1q/AAFk0mMYxjHqUpSlH8AAsD4oBH50+Aj4GYX0Mot4w4PzAAX42zMzTf///wjf/7GM0a1rXC973vejAAKSv3ve976c5znOaAqAAHNcM23X5eD9qYAE/dmZma////843/+" +
        "ihIt73vlYxjGMQwAERf4AAADAAR/////sb3/++7H8z0/6yO+qyNx19E8ADclMzM0Kqqqomz/+qRM+ta1xPe973oIAFPQXOc5zomta1rVJKAAGdFglnm12GE4oAH36gAACNVVVQ5f/760" +
        "DxjGMqta1rWXAATmwhCEIQmMYxjGG1wAAgz+Cj13L8XrAAt/nmZmmf///nz//hs1KEIQjHOc5zkoAC3zW1rWtbmEIQhBPrgAGhOxLqySX3p4ADSmRmZnUqqqpFX/+9TUt0hMcccoOOOO" +
        "OOGIAJ7i+QJwJq1atWwXLly5cuWFADdEgBq9oAAAd////On//bJTCYBF1118WWWWWWR4AGSckNcCzdu3bt6HDhw4cOF0A+YigBbRmZmZ/////Zj/+f7x9rWthb3ve9zoANmfQhCEIUGM" +
        "YxjBI5//scXEnb6dNM3SxGNl+qFADUf+Zmah///+r//8LuYta1rne973vKAAndM1rWtbAEIQhB1jYABA7upcfHrlKADQOJmZnT///+0//+Z4qwalXXXXpZZZZZZNAAXeM2DRO5w4cOHP" +
        "Zs2bNmzVHZqcgAD928zM0T///9OAF1xTJiamGvQHHqZ6mepnqZ692vdr3a92vdr3a92ry///puDTLNIeoAReUXlF5ReUYG2BtgbYG2BtgbYG15fZ3gdmADU+ZmZnVVVVUTv/7FQsJSlK" +
        "qQhCEH1gA+fipSlKU5ve973Uwx/NwA/dvMzNE////t//+caS73vfExjGMYpAAVIyTnOc55LWta1iewwv+EAGbsDMzOmqqqo+f/1ZqqCEIRPnOc5ytACSjUQhCEI3GMYxiHh7mLAChKEz" +
        "Mz5////Xf/7vige974CMYxjF1AA+P21rWtbGEIQhBqYKwDjYAb7eAAAH1VVVO9//OUqgIQhFec5znKMAL+lClKUpVu973vZ2MRyEANZmszM29VVVSm//m7TLrWtcj3ve95uAGN/lrWta" +
        "3UIQhCBeGqmiAGvp5mZoSqqqpX//zFOOta1sPe973vNf//4h1w8/CI32VwNcDXA1wNcQvELxC8QvELxC8QvA4I32IALeAMzM2aqqqol//pkadCEISDnOc5xsAF4bWta1rYQhCEIOAHdC" +
        "MAJiZszM13///5UAT/Sr+kv6I/OnyZ8mfJnyZ8nvJ7ye8nvJ7ye8nvJoAAnVW73ve+GOc5zm9o6P5gBMibMzNIqqqqd//9l6zCEIQrnOc5zaAApS1YxjGMjpSlKUYLL+maGAFGD9RVex" +
        "TOBgYGBlgYGBgYGBf5P/9hOJqUpSwEIQhB2gArAoCEIQhNYxjGMHQP8db//+rctV+1JSlgHjB4weMHjB6lepXqV6lepXqV6lebx//GkmPWta5nve97y0AP7fa1rWtfCEIQhDPN//+RGM" +
        "ZeBM8EoPmXqo6qOqj3GbSG0htIch20htIa///+wJAsYxjLrWta1kgAWPXb3ve+Buc5znE8LcK7//7rOHTccd3XwP9D/Q/0P9EFQBUAVAFQBUAVAFQAo3/8+7bpznOqKUpSk5wA2SHIQh" +
        "CEWjGMYxT7IR8LcaRmdshsYuhxdrx/Xt9PzwKABlu8zMzpVVVVH//8mCAYxjGbWta1rEAA9wfOc5zneta1rWVgABKzKp/ivEKgycAXtcmZmgP///89//M6eFznOgaUpSlF8AOfWDnOc5" +
        "3bWta1lKJ8lYAPv1AAAEaqqqov//dtR/e976GMYxjDQAJxayEIQhEoxjGMUvJl6HYAHbZAAACFVVVUc//vvAwMYxkNrWta0SAEqAUpSlKV3ve972AwAGeeqOmxqfiQAELK2ZmaxVVVUz" +
        "f/2/InpSlK8QhCEHfAClLVjGMYyOlKUpRl4ADoj7gESVsAACt/9XouRHBtbW1teGBgYGBgYFyT/+mgr4hCEOZznOcyIAZjG0pSlKbXve97wfAFtZL2xVvEP7/soqBtzmDX+RNEAJRSZm" +
        "Zo////+1//r6vI5znP1KUpSjcAFun+c5znP9a1rWrS9AAJ+R7jpt6B9QAfKQxEc0wqZGRkZGw8PDw8PDw5k//vvAwMYxkNrWta0SAEqAUpSlKV3ve972cQA9MELlb7CKwX3MSV5WDZBo" +
        "OJfgBXeMzMzlVVVVKX/9Cr8wAAAM/////vwA1q5nOc5z1Wta1rDBgAB4MhPcOBGCC0VADFSYAAA3VVVU7//5XVkEIQhfOc5zmqAB4VE973vfdnOc5y53v/8qZ63+2bYlU2DgkrzgdQAk" +
        "18AAAKVVVVQv/+wJAsYxjLrWta1kgAWyqAAAAwABf////936AAJ/qWbAG6cMHI4ARh7AAAE6qqqod//aER7e976WMYxjC8AK2Cb3ve9+uc5znKa4AAdh+PGqDJByagBZX5mZmyqqqqfX" +
        "/8+7bpznOqKUpSk5wA2po1rWtbEEIQhBv2YADOiwSzzMm1kcAHuaAAADAiqqqqb//7xaxCEIQznOc5zQABM87Oc5znita1rWMhAFIgBc8FmZmzqqqqe3/83aZda1rke973vNwA42zGMY" +
        "xjVKUpSkyonoEdgAW4NAAAGaqqqnx//OjskhCELpznOc1kAOD5ZSlKUs/e973peEg9cARWgzMzRqqqqegAn2grFClziebDzpkwbSVj9g35re8ba84susHe35u8r//8qG5Zsj5Eh3glTo" +
        "bQCxnH+W1/jU0Oz7eh9D+XCWSzfiADjtkzM0MqqqqN//4TsQUpSlsIQhCDeACNpoIQhCEtjGMYwmi/UYADW4GZmaiqqqqOv/4yshXve+bjGMYw7gCE0/GMYxjpKUpSkRiwwNADM5LMzN" +
        "sqqqqQf/5GbPIQhCic5znN3AB9ec5znOda1rWtaADEDRADmFszM0NVVVU4f/4ODerWtbX3ve97NACPHu973vfjnOc5ysjyUa8wAoShMzM+f///63/+pGCoxjGSWta1rPQAY0DZznOdAV" +
        "rWtarEmzYgAw2KzMzaf///5n/+XPlbWta83ve97uwAd5gYxjGMgpSlKUdEEinNIAMNiszM2n///3cAb3A2GA0X1U7LdDXQ10NdDXSlUpVKVSlUpVKVSlUNwA7zAxjGMZBSlKUo5Ba7pA" +
        "BhsVmZm0////zP/8ufK2ta15ve973dgA7zAxjGMZBSlKUo5BZjRABhsVmZm0////zP/8ufK2ta15ve973dgA7zAxjGMZBSlKUo5BpEogBzC2ZmaGqqqqcP/8HBvVrWtr73ve9mgBHj3e" +
        "973vxznOc5VCC7TAChKEzMz5////rf/6kYKjGMZJa1rWs9ABjQNnOc50BWta1qsS5BCADDYrMzNp////mf/5c+Vta1rze973u7AB3mBjGMYyClKUpR1DgAAADMwoAa8FuBR7oln//r/V" +
        "83a6666+++++++8qH//yJq4AvbXHahGQewb3ve973wOg6DoOg6DoOenP/2Qc///f/vftfS8LwvC8Lw/D8Pw/D8Pw/DvAmZntwgGLhmZmbVVVVRU39rFf/+/R42H1bbbbm222222vt/mg" +
        "oA8w/IXZxRo0aNHCBAgQIEB4vOGAA6BoUshRuvoQABAUKB85cg2urq6usrq6urq6uremtPH//4EqL73vfgxjGMYGFV9gC/J5CEIQh0YxjGLHAABpP8zmj03Iw7vSqttisO6/DEcoAAs7" +
        "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" +
        "c50zWta1pvOAAHXHI6UHnQKeIAV7WmZmfv///9J//Q6BQYxjJbWta1nUANYw7Wta1u4QhCEDYh//oXJhZ9kJs5SalFSHXtCAGwmwAAB5VVVUef/xaHfc5zoOlKUpRVAEMmL3ve98+c5z" +
        "nM67AAHbQZgXTcz5/reiAFgvWZmbJVVVUpf/0DOwznOduUpSlJTADZIchCEIRaMYxjEviQUmADclMzM0KqqqqOf/4kmlQhCEo5znOcRACJPTnOc50LWta1qd3mWAB4J38szwTtMTExMU" +
        "9vb29vb29lD//Bwb1a1ra+973vZoAR493ve978c5znOYAADhZGcZN7d7OObAuyzCb1s8fMuAGvp5mZoSqqqpzf/xd4AjGMZxa1rWsLAEKota1rWuhCEIQff7AAHfZqGafCenIAcC1fXN" +
        "JFZWlpaWmFpaWlpaWlm8//xnRTSlKVohCEIPSAEIrO973vgDnOc5yZAA3OyP6MnItR1Fa7ZWKbd9sL2RwBTMYAAAXVVVVK3/9MFPfe98EGMYxi2wAzV5c5znOqa1rWtHygABaffpawzN" +
        "kNznACMPYAAAnVVVVDv/7QiPb3vfSxjGMYXgBWwTe973v1znOc5TSP/9nLEnyIgXbqFyv5ou/yoAeizmZmiKqqqm7/+9h2mc5zrylKUpM4AS/jAAAAMABP////+SaAAJ/qWbAG54Jlno" +
        "AO2yAAAEKqqqo5//feBgYxjIbWta1okAJUApSlKUrve973sGYAAYhc2dezQzd/gA/8KZmZ4VVVVNX/91a7jnOdDUpSlKIwAnlCve97355znOcrLYAB1brgvbnDdwqAFYLszM0tVVVUpf" +
        "/0Zx3YxjG/Wta1qXADSwpjGMYyalKUpRWw//52XlKXJ8YpnF/rCyzSDgBSAUAAAW////1f/9PXsc5znalKUpSWAAyXKOc5znbta1rWanoABaJY7lKd0f10AHQ7pmZob////G//wNE497" +
        "3wkYxjGMLv//1s2rQequUw45yOcjnI5yOu/rv67+u/rv67+u/nMHpoEAMXDMzM2qqqqpD//ldWQQhCF85znOaoAHhUT3ve992c5znLUA0XAgACNMOZmaN///+dAFG0bIosCgMjYqhuob" +
        "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");

    /// <summary>An image sequence of 3 RGB images of 72x66 of the block pattern, coded as pictures of 72x72 with a conformance window, that each last 40/1000 s, played 3 times.</summary>
    public static byte[] Sequence => Convert.FromBase64String(
        "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" +
        "knHc0gAIUcAAACVVVVPq///WqT73vfAxjGMYuAAAkJdc5znOma1rWtQoMAAXUFWB4zbyWAAkZTEk8Snbf39/f4ioqKioqKigyX/+68f2MYxn1rWtavwABZNJjGMYx6lKUpR/AALA+KAR" +
        "+dPgI+BmF9DKLeMOD8wAF+NszM03///8I3/+xjNGta1wve973owACkr973ve+nOc5zmgKgABzXDNt1+Xg/amABP3ZmZmv////ON//ooSLe975WMYxjEMABEX+AAAAwAEf////7G9//vu" +
        "x/M9P+sjvqsjcdfRPAA3JTMzNCqqqqJs//qkTPrWtcT3ve96CABT0FznOc6JrWta1SSgABnRYJZ5tdhhOKAB9+oAAAjVVVUOX/++tA8YxjKrWta1lwAE5sIQhCEJjGMYxhtcAAIM/go9" +
        "dy/F6wALf55mZpn///58//4bNShCEIxznOc5KAAt81ta1rW5hCEIQT64ABoTsS6skl96eAA0pkZmZ1KqqqRV//vU1LdITHHHKDjjjjjhiACe4vkCcCatWrVsFy5cuXLlhQA3RIAavaAA" +
        "AHf///zp//2yUwmARdddfFlllllkeABknJDXAs3bt27ehw4cOHDhdAPmIoAW0ZmZmf////2Y//n+8fa1rYW973vc6ADZn0IQhCFBjGMYwSOf/7HFxJ2+nTTN0sRjZfqhQA1H/mZmof//" +
        "/q///C7mLWta53ve97ygAJ3TNa1rWwBCEIQdY2AAQO7qXHx65SgA0DiZmZ0////tP//meKsGpV1116WWWWWWTQAF3jNg0TucOHDhz2bNmzZs1R2anIAA/dvMzNE////TgBdcUyYmphr0" +
        "Bx6mepnqZ6mevdr3a92vdr3a92vdq8v//6bg0yzSHqAEXlF5ReUXlGBtgbYG2BtgbYG2BteX2d4HZgA1PmZmZ1VVVVE7/+xULCUpSqkIQhB9YAPn4qUpSlOb3ve91MMfzcAP3bzMzRP/" +
        "//7f//nGku973xMYxjGKQAFSMk5znOeS1rWtYnsML/hABm7AzMzpqqqqPn/9WaqghCET5znOcrQAko1EIQhCNxjGMYh4e5iwAoShMzM+f///13/+74oHve+AjGMYxdQAPj9ta1rWxhCE" +
        "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" +
        "GQUpSlKOQWY0QAYbFZmZtP///8z//LnytrWteb3ve93YAO8wMYxjGQUpSlKOQaRKIAcwtmZmhqqqqnD//Bwb1a1ra+973vZoAR493ve978c5znOVQgu0wAoShMzM+f///63/+pGCoxjG" +
        "SWta1rPQAY0DZznOdAVrWtarEuQQgAw2KzMzaf///5n/+XPlbWta83ve97uwAd5gYxjGMgpSlKUcx5EkAGGxWZmbT////M//6/RiPDRTTTUL777777VAB5h+QuzijRo0aOECBAgQIDqB" +
        "wKDAChKEzMz5////rf/6kYKjGMZJa1rWs9ABjQNnOc50BWta1qsS9WSADDYrMzNp////mf/5c+Vta1rze973u7AB3mBjGMYyClKUpRyDtumAFCUJmZnz////W//1IwVGMYyS1rWtZ6AD" +
        "GgbOc5zoCta1rVYmE8iAHMLZmZoaqqqpw//wcG9Wta2vve972aAEePd73ve/HOc5zlUJC5IAMNiszM2n///+Z//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHIPNAYAUJQmZmfP///9b//U" +
        "jBUYxjJLWta1noAMaBs5znOgK1rWtViYjkIAcwtmZmhqqqqnD//Bwb1a1ra+973vZoAR493ve978c5znOVajVP+ZABmdIiXVjv37e3t7ge3t7e3t7eur//LnytrWteb3ve93YAO8wMYx" +
        "jGQUpSlKOlq2A8ZCADDYrMzNp////mf/5c+Vta1rze973u7AB3mBjGMYyClKUpRyCWfyADDYrMzNp////mf/5c+Vta1rze973u7AB3mBjGMYyClKUpRyCuIyADDYrMzNp////mf/5c+V" +
        "ta1rze973u7AB3mBjGMYyClKUpRyC7YSADDYrMzNp////mf/5c+Vta1rze973vZf//7vZ3a/cu7WmuBrga4GuBroC6AugLoC6AugLoC4IGUpiAHMLZmZoaqqqpw//wcG9Wta2vve972a" +
        "AEePd73ve/HOc5zlUID1MAKEoTMzPn///+t//qRgqMYxklrWtaz0AGNA2c5znQFa1rWq6CsN07zX//9RHagVpjFE0NbDWw1sNbDai2otqLai2otqLai2OH/+Dg3q1rW1973vezQAjx7v" +
        "e97345znOcqgP+MYAUJQmZmfP///9b//UjBUYxjJLWta1noAMaBs5znOgK1rWtV1cAAALj1tZGF0AAAPdCgBrwW4FHuiWf//k/8l/HfhvW9b1vW9f1/X9f1/X9f1yof//ImrgC9o/dGc" +
        "O/GHadp2naeZZlmWZZlmWZXXP/2Qc///p/9N/Rfmvivivivivi/i/i/i/i/i/i/i7wJmZ7cIBi4ZmZm1VVVUVN/axX//v0eNh9W2225ttttttr7f5oKAPMPyF2cUaNGjRwgQIECBAeLz" +
        "hgAOgaFLIUbr6EAAQFCgfOXINrq6urrK6urq6urq3prTx//+BKi+9734MYxjGBhVfYAvyeQhCEIdGMYxixwAAaT/M5o9NyMO70qrbYrDuvwxHKAALOymZmcv///4KbS//+Hvc5znOlKU" +
        "pSlAHWMAH15znOc51rWta1pG14AA8yeDZTozprAAFfygAABiqqqm2Pnf/9TaXlqOeeee8888888oT2ABxj8Ru4jbt27dvw4cOHDhwrfq5wACMtzMzNaqqqpKmS//6FvqGRWIIIJK6666" +
        "66O5eAKZblsZdWXLly5h+/fv379903GyGdgAGco5mZoNVVVQ2H//9kCie9736MYxjF/GOANepWta1raQhCEIMkMAAFKMUsoAW41+dAAUzGAAAF1VVVG2Z//n7L+UpSm0IQhCCEAAFHad" +
        "a1rWuxCEIQfj4AACpPa8XLwuMYACgCh48vsKUGBgYGEBgYGBgYGA8zv/+1xyVrWtf73ve91gAAfXnOc5znWta1rWgAACBk2B8dAxxIUahjE9M0qScdzSAAhRwAAAJVVVU+r//9apPve9" +
        "8DGMYxi4AACQl1znOc6ZrWta1CgwABdQVYHjNvJYACRlMSTxKdt/f39/iKioqKioqKDJf/7rx/YxjGfWta1q/AAFk0mMYxjHqUpSlH8AAsD4oBH50+Aj4GYX0Mot4w4PzAAX42zMzTf/" +
        "//wjf/7GM0a1rXC973vejAAKSv3ve976c5znOaAqAAHNcM23X5eD9qYAE/dmZma////843/+ihIt73vlYxjGMQwAERf4AAADAAR/////sb3/++7H8z0/6yO+qyNx19E8ADclMzM0Kqqq" +
        "omz/+qRM+ta1xPe973oIAFPQXOc5zomta1rVJKAAGdFglnm12GE4oAH36gAACNVVVQ5f/760DxjGMqta1rWXAATmwhCEIQmMYxjGG1wAAgz+Cj13L8XrAAt/nmZmmf///nz//hs1KEIQ" +
        "jHOc5zkoAC3zW1rWtbmEIQhBPrgAGhOxLqySX3p4ADSmRmZnUqqqpFX/+9TUt0hMcccoOOOOOOGIAJ7i+QJwJq1atWwXLly5cuWFADdEgBq9oAAAd////On//bJTCYBF1118WWWWWWR4" +
        "AGSckNcCzdu3bt6HDhw4cOF0A+YigBbRmZmZ/////Zj/+f7x9rWthb3ve9zoANmfQhCEIUGMYxjBI5//scXEnb6dNM3SxGNl+qFADUf+Zmah///+r//8LuYta1rne973vKAAndM1rWtb" +
        "AEIQhB1jYABA7upcfHrlKADQOJmZnT///+0//+Z4qwalXXXXpZZZZZZNAAXeM2DRO5w4cOHPZs2bNmzVHZqcgAD928zM0T///9OAF1xTJiamGvQHHqZ6mepnqZ692vdr3a92vdr3a92r" +
        "y///puDTLNIeoAReUXlF5ReUYG2BtgbYG2BtgbYG15fZ3gdmADU+ZmZnVVVVUTv/7FQsJSlKqQhCEH1gA+fipSlKU5ve973Uwx/NwA/dvMzNE////t//+caS73vfExjGMYpAAVIyTnOc" +
        "55LWta1iewwv+EAGbsDMzOmqqqo+f/1ZqqCEIRPnOc5ytACSjUQhCEI3GMYxiHh7mLAChKEzMz5////Xf/7vige974CMYxjF1AA+P21rWtbGEIQhBqYKwDjYAb7eAAAH1VVVO9//OUqg" +
        "IQhFec5znKMAL+lClKUpVu973vZ2MRyEANZmszM29VVVSm//m7TLrWtcj3ve95uAGN/lrWta3UIQhCBeGqmiAGvp5mZoSqqqpX//zFOOta1sPe973vNf//4h1w8/CI32VwNcDXA1wNcQ" +
        "vELxC8QvELxC8QvA4I32IALeAMzM2aqqqol//pkadCEISDnOc5xsAF4bWta1rYQhCEIOAHdCMAJiZszM13///5UAT/Sr+kv6I/OnyZ8mfJnyZ8nvJ7ye8nvJ7ye8nvJoAAnVW73ve+GO" +
        "c5zm9o6P5gBMibMzNIqqqqd//9l6zCEIQrnOc5zaAApS1YxjGMjpSlKUYLL+maGAFGD9RVexTOBgYGBlgYGBgYGBf5P/9hOJqUpSwEIQhB2gArAoCEIQhNYxjGMHQP8db//+rctV+1JS" +
        "lgHjB4weMHjB6lepXqV6lepXqV6lebx//GkmPWta5nve97y0AP7fa1rWtfCEIQhDPN//+RGMZeBM8EoPmXqo6qOqj3GbSG0htIch20htIa///+wJAsYxjLrWta1kgAWPXb3ve+Buc5zn" +
        "E8LcK7//7rOHTccd3XwP9D/Q/0P9EFQBUAVAFQBUAVAFQAo3/8+7bpznOqKUpSk5wA2SHIQhCEWjGMYxT7IR8LcaRmdshsYuhxdrx/Xt9PzwKABlu8zMzpVVVVH//8mCAYxjGbWta1rE" +
        "AA9wfOc5zneta1rWVgABKzKp/ivEKgycAXtcmZmgP///89//M6eFznOgaUpSlF8AOfWDnOc53bWta1lKJ8lYAPv1AAAEaqqqov//dtR/e976GMYxjDQAJxayEIQhEoxjGMUvJl6HYAHb" +
        "ZAAACFVVVUc//vvAwMYxkNrWta0SAEqAUpSlKV3ve972AwAGeeqOmxqfiQAELK2ZmaxVVVUzf/2/InpSlK8QhCEHfAClLVjGMYyOlKUpRl4ADoj7gESVsAACt/9XouRHBtbW1teGBgYG" +
        "BgYFyT/+mgr4hCEOZznOcyIAZjG0pSlKbXve97wfAFtZL2xVvEP7/soqBtzmDX+RNEAJRSZmZo////+1//r6vI5znP1KUpSjcAFun+c5znP9a1rWrS9AAJ+R7jpt6B9QAfKQxEc0wqZG" +
        "RkZGw8PDw8PDw5k//vvAwMYxkNrWta0SAEqAUpSlKV3ve972cQA9MELlb7CKwX3MSV5WDZBoOJfgBXeMzMzlVVVVKX/9Cr8wAAAM/////vwA1q5nOc5z1Wta1rDBgAB4MhPcOBGCC0VA" +
        "DFSYAAA3VVVU7//5XVkEIQhfOc5zmqAB4VE973vfdnOc5y53v/8qZ63+2bYlU2DgkrzgdQAk18AAAKVVVVQv/+wJAsYxjLrWta1kgAWyqAAAAwABf////936AAJ/qWbAG6cMHI4ARh7A" +
        "AAE6qqqod//aER7e976WMYxjC8AK2Cb3ve9+uc5znKa4AAdh+PGqDJByagBZX5mZmyqqqqfX/8+7bpznOqKUpSk5wA2po1rWtbEEIQhBv2YADOiwSzzMm1kcAHuaAAADAiqqqqb//7xa" +
        "xCEIQznOc5zQABM87Oc5znita1rWMhAFIgBc8FmZmzqqqqe3/83aZda1rke973vNwA42zGMYxjVKUpSkyonoEdgAW4NAAAGaqqqnx//OjskhCELpznOc1kAOD5ZSlKUs/e973peEg9cA" +
        "RWgzMzRqqqqegAn2grFClziebDzpkwbSVj9g35re8ba84susHe35u8r//8qG5Zsj5Eh3glTobQCxnH+W1/jU0Oz7eh9D+XCWSzfiADjtkzM0MqqqqN//4TsQUpSlsIQhCDeACNpoIQhC" +
        "EtjGMYwmi/UYADW4GZmaiqqqqOv/4yshXve+bjGMYw7gCE0/GMYxjpKUpSkRiwwNADM5LMzNsqqqqQf/5GbPIQhCic5znN3AB9ec5znOda1rWtaADEDRADmFszM0NVVVU4f/4ODerWtb" +
        "X3ve97NACPHu973vfjnOc5ysjyUa8wAoShMzM+f///63/+pGCoxjGSWta1rPQAY0DZznOdAVrWtarEmzYgAw2KzMzaf///5n/+XPlbWta83ve97uwAd5gYxjGMgpSlKUdEEinNIAMNis" +
        "zM2n///3cAb3A2GA0X1U7LdDXQ10NdDXSlUpVKVSlUpVKVSlUNwA7zAxjGMZBSlKUo5Ba7pABhsVmZm0////zP/8ufK2ta15ve973dgA7zAxjGMZBSlKUo5BZjRABhsVmZm0////zP/8" +
        "ufK2ta15ve973dgA7zAxjGMZBSlKUo5BpEogBzC2ZmaGqqqqcP/8HBvVrWtr73ve9mgBHj3e973vxznOc5VCC7TAChKEzMz5////rf/6kYKjGMZJa1rWs9ABjQNnOc50BWta1qsS5BCA" +
        "DDYrMzNp////mf/5c+Vta1rze973u7AB3mBjGMYyClKUpRzHkSQAYbFZmZtP///8z//r9GI8NFNNNQvvvvvvtUAHmH5C7OKNGjRo4QIECBAgOoHAoMAKEoTMzPn///+t//qRgqMYxklr" +
        "Wtaz0AGNA2c5znQFa1rWqxL1ZIAMNiszM2n///+Z//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHIO26YAUJQmZmfP///9b//UjBUYxjJLWta1noAMaBs5znOgK1rWtViYTyIAcwtmZmhqq" +
        "qqnD//Bwb1a1ra+973vZoAR493ve978c5znOVQkLkgAw2KzMzaf///5n/+XPlbWta83ve97uwAd5gYxjGMgpSlKUcg80BgBQlCZmZ8////1v/9SMFRjGMkta1rWegAxoGznOc6ArWta1" +
        "WJiOQgBzC2ZmaGqqqqcP/8HBvVrWtr73ve9mgBHj3e973vxznOc5VqNU/5kAGZ0iJdWO/ft7e3uB7e3t7e3t66v/8ufK2ta15ve973dgA7zAxjGMZBSlKUo6WrYDxkIAMNiszM2n///+" +
        "Z//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHIJZ/IAMNiszM2n///+Z//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHIK4jIAMNiszM2n///+Z//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHIL" +
        "thIAMNiszM2n///+Z//lz5W1rWvN73ve9l///u9ndr9y7taa4GuBrga4GugLoC6AugLoC6AugLggZSmIAcwtmZmhqqqqnD//Bwb1a1ra+973vZoAR493ve978c5znOVQgPUwAoShMzM+" +
        "f///63/+pGCoxjGSWta1rPQAY0DZznOdAVrWtaroKw3TvNf//1EdqBWmMUTQ1sNbDWw1sNqLai2otqLai2otqLY4f/4ODerWtbX3ve97NACPHu973vfjnOc5yqA/4xgBQlCZmZ8////1" +
        "v/9SMFRjGMkta1rWegAxoGznOc6ArWta1XVwAAAPTSgBrwW4FHuhWgD+T8fvvPPPPRRRRRRRQVD//5E1cAXfowtb3vfFhhhhhheKf/sg5//+Z/L/H9+/fv38HBwcHBwcHBeBMzPbhAMX" +
        "DMzM2qqqqipv7WK//9+jxsPq2223Nttttttfb/NBQB5h+QuzijRo0aOECBAgQIDxecMAB0DQpZCjdfQgACAoUD5y5BtdXV1dZXV1dXV1dW9NaeP//wJUX3ve/BjGMYwMKr7AF+TyEIQh" +
        "DoxjGMWOAADSf5nNHpuRh3elVbbFYd1+GI5QABZ2UzMzl////BTaX//w97nOc50pSlKUoA6xgA+vOc5znOta1rWtI2vAAHmTwbKdGdNYAAr+UAAAMVVVU2x87//qbS8tRzzzz3nnnnnn" +
        "lCewAOMfiN3Ebdu3bt+HDhw4cOFb9XOAARluZmZrVVVVJUyX//Qt9QyKxBBBJXXXXXXR3LwBTLctjLqy5cuXMP379+/fvum42QzsAAzlHMzNBqqqqGw///sgUT3ve/RjGMYv4xwBr1K1" +
        "rWtbSEIQhBkhgAApRillAC3GvzoACmYwAAAuqqqo2zP/8/ZfylKU2hCEIQQgAAo7TrWta12IQhCD8fAAAVJ7Xi5drl/Btb//8IIINkgLX2sYUGFBhQYUGKYimIpiKYimIpiKYhSY//8p" +
        "yhe973+MYxjF6AABemNSlKUpre973u4A///dhM04jioyghximkb/DsoXYIcYb/CqGHOvmKkABCjgAAASqqqp9X//61Sfe974GMYxjFwAAEhLrnOc50zWta1qCwACP71JZN99mABUgcED" +
        "XY0GbGxsbIGxsbGxsbGfcX/9ZPDVKUpeCEIQgzwADoU4hCEIReMYxjEpAAfRuJTAYdqwWlTId4tBr3gcGJgAXabmZmgKqqqbkf/7CcTUpSlgIQhCDtAAKqRlKUpSn973ve6zDAAJJx+s" +
        "WxiEGRNoAGeimZmbaqqqmnn/+JJpUIQhKOc5znEQAFb/9KUpSml73ve8UQAD2jhjpaJ3NjAAeqaZmZu////td//019vnOc6kpSlKTsAAsHNKUpSlV73ve9u+AAmUOS8fY8ET3wAIndAA" +
        "ACaqqqlxf/7rx/YxjGfWta1q/AAVkO973vfDnOc5zhB4AAaVgHG33G75XAA0aWzMzbf///kZ//dlw9SlKXAhCEINkADNXlznOc6prWta0wWAAGj/m6XkphJiUAEAtgAABH///+B//+yV" +
        "d0JZtttt7bbbbbbSAAMBB/fYcK5cuXLr379+/fv2aIEaMgBtVwAAAwHqqqqeB//2m0YkP40002e++++++PP//71S3nbdrLgwx4MeDHgx4MkXJFyRckXJFyRckXHiBEDtABuxBmZm4qqq" +
        "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" +
        "or//dALyUpSnkIQhCBIAJ/JHOc5zp2ta1rScRy1oAZnJZmZtlVVVSD//IzZ5CEIUTnOc5u4APrznOc5zrWta1rQAY/qIAcwtmZmhqqqqnD//Bwb1a1ra+973vZoAR493ve978c5znOVQ" +
        "iNYwAoShMzM+f///63/+pGCoxjGSWta1rPQAY0DZznOdAVrWtarEroggAw2KzMzaf///5n/+XPlbWta83ve97uwAd5gYxjGMgpSlKUcg9zJgBQlCZmZ8////1v/9SMFRjGMkta1rWegA" +
        "xoGznOc6ArWta1XQQSw8ZCADDYrMzNp////mf/5c+Vta1rze973u7AB3mBjGMYyClKUpRyCxJiADDYrMzNp////mf/5c+Vta1rze973u7AB3mBjGMYyClKUpRy7hLCr//7W/GqwZ3nCG" +
        "KGihooaKGioDqA6gOoDqA6gOoDpTP/8ufK2ta15ve973dgA7zAxjGMZBSlKUpFf///jffGqcVhhaHth7Yer3l15deNHeJ3id4neJ3idvL0FsgA47ZMzNDKqqqjf/+E7EFKUpbCEIQg3g" +
        "AjaaCEIQhLYxjGMJo34VgBYbDMzNL////0v/9BRwVrWtn73ve9rgA2SHIQhCEWjGMYxL5iONgB4v8zMzuqqqqcf/73DirWtbH3ve97hABLe0YxjGMspSlKUJsS9MAKEoTMzPn///+t//" +
        "3r2ouyMoooqGmmmmmlmADJuKOqBFrVq1au8uXLly5cIsT8cjKVf//a341WDO84QxQ0UNFDRQ0VAdQHUB1AdQHUB1AdKZ//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHIKqWIAMNiszM2n/" +
        "//+Z//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHILwrIAMNiszM2n///+Z//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHIMbQEAOYWzMzQ1VVVTh//g4N6ta1tfe973s0AI8e73ve9+Oc5zn" +
        "KoP4CYAUJQmZmfP///9b//UjBUYxjJLWta1noAMaBs5znOgK1rWtViWlcQAYbFZmZtP///8z//LnytrWteb3ve93YAO8wMYxjGQUpSlKOQdUkwAoShMzM+f///63/+pGCoxjGSWta1rP" +
        "QAY0DZznOdAVrWtarSczuhSADM6REurHfv29vb3A9vb29vb29dX/+XPlbWta83ve97uwAd5gYxjGMgpSlKUdMKQCflCAHMLZmZoaqqqpw//wcG9Wta2vve972aAEePd73ve/HOc5zlUF" +
        "yCEAGGxWZmbT////M//y58ra1rXm973vd2ADvMDGMYxkFKUpSjkFfJEAGGxWZmbT////M//y58ra1rXm973vd2ADvMDGMYxkFKUpSjkF4VkAGGxWZmbT////M//y58ra1rXm973vd2AD" +
        "vMDGMYxkFKUpSjkGZRCAHMLZmZoaqqqpw//wcG9Wta2vve972aAEePd73ve/HOc5zlUIKIMAKEoTMzPn///+t//qRgqMYxklrWtaz0AGNA2c5znQFa1rWqxLkEIAMNiszM2n///+Z//l" +
        "z5W1rWvN73ve7sAHeYGMYxjIKUpSlHIEYxEAGGxWZmbT////M//y58ra1rXm973vd2ADvMDGMYxkFKUpSjqHAAAPaCgBrwWoFHuiWf//sf9h/WfpPlPlPlPlPlflflflflflflflSof/" +
        "/Ina///R+onTt82bNmzxo0aNGjRhs//Y90APnxjGMYxnOc5znN4EzM9uEAxcMzMzaqqqqKm/tYr//36PGw+rbbbc22222219v80FAHmH5C7OKNGjRo4QIECBAgPF5wwAHQNClkKN19CA" +
        "AIChQPnLkG11dXV1ldXV1dXV1b01p4///AlRfe978GMYxjAwqvsAX5PIQhCEOjGMYxY4AANJ/mc0em5GHd6VVtsVh3X4YjlAAFnZTMzOX///8FNpf//D3uc5znSlKUpSgDrGAD685znO" +
        "c61rWta0ja8AAeZPBsp0Z01gACv5QAAAxVVVTbHzv/+ptLy1HPPPPeeeeeeeUJ7AA4x+I3cRt27du34cOHDhw4Vv1c4ABGW5mZmtVVVUlTJf/9C31DIrEEEElddddddHcvAFMty2MurL" +
        "ly5cw/fv379++6bjZDOwADOUczM0GqqqobD//+yBRPe979GMYxi/jHAGvUrWta1tIQhCEGSGAAClGKWUALcI3OgAKZjAAAC6qqqjYAAMPitvFz8K/g/XSlxleT3Ytz4cNHh51aclnbxz" +
        "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" +
        "xjGMZBSlKUo6JZFOaQAYbFZmZtP///8z//LnytrWteb3ve93YAO8wMYxjGQUpSlKOQXOaQAYbFZmZtP///8z//LnytrWteb3ve93YAO8wMYxjGQUpSlKOQWSqQAYbFZmZtP///8z//Ln" +
        "ytrWteb3ve93YAO8wMYxjGQUpSlKOQaqaIAcwtmZmhqqqqnD//Bwb1a1ra+973vZoAR493ve978c5znOVQhUowAoShMzM+f///63/+pGCoxjGSWta1rPQAY0DZznOdAVrWtarEu2EgAw" +
        "2KzMzaf///5n/+XPlbWta83ve97uwAd5gYxjGMgpSlKUcx6/EAGGxWZmbT////M//6/RiPDRTTTUL777777VAB5h+QuzijRo0aOECBAgQIDqByITAChKEzMz5////rf/6kYKjGMZJa1r" +
        "Ws9ABjQNnOc50BWta1qsS/tyADDYrMzNp////mf/5c+Vta1rze973u7AB3mBjGMYyClKUpRyDw4mAFCUJmZnz////W//1IwVGMYyS1rWtZ6ADGgbOc5zoCta1rVYmHUCAHMLZmZoaqqq" +
        "pw//wcG9Wta2vve972aAEePd73ve/HOc5zlUJKyIAMNiszM2n///+Z//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHIPWeYAUJQmZmfP///9b//UjBUYxjJLWta1noAMaBs5znOgK1rWtVi" +
        "Y2gIAcwtmZmhqqqqnD//Bwb1a1ra+973vZoAR493ve978c5znOVapVP+ZABmdIiXVjv37e3t7ge3t7e3t7eur//LnytrWteb3ve93YAO8wMYxjGQUpSlKOl22A8ZCADDYrMzNp////mf" +
        "/5c+Vta1rze973vZf//7vZ3a/cu7WmuBrga4GuBroC6AugLoC6AugLoC4IExukAGGxWZmbT////M//y58ra1rXm973vd2ADvMDGMYxkFKUpSjkFguEAGGxWZmbT////M//y58ra1rXm9" +
        "73vd2ADvMDGMYxkFKUpSjkF7fEAGGxWZmbT////M//y58ra1rXm973vd2ADvMDGMYxkFKUpSjkGXziAHMLZmZoaqqqpw//wcG9Wta2vve972aAEePd73ve/HOc5zlUIIkMAKEoTMzPn/" +
        "//+t//qRgqMYxklrWtaz0AGNA2c5znQFa1rWqxLifIAMNiszM2n///+Z//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHIEYxEAGGxWZmbT////M//y58ra1rXm973vey///3ezu1+5d2tNc" +
        "DXA1wNcDXQF0BdAXQF0BdAXQFwcO");

    /// <summary>
    /// An image sequence of 4 RGB images of 64x64 of the block pattern that each last 25/600 s, repeated forever, coded with
    /// pictures that are predicted from each other and that are not in presentation order: the samples are the images 0,
    /// 2, 1 and 3 (composition offsets 0, 25, -25 and 0).
    /// </summary>
    public static byte[] ReorderedSequence => Convert.FromBase64String(
        "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" +
        "KbQhCEIIQAAUdp1rWta7EIQhB+PgAAKk9rxcvC4xgAKAKHjy+wpQYGBgYQGBgYGBgYDzO//7XHJWta1/ve973WAAB9ec5znOda1rWtaAAAIGTYHx0DHEhRqGMT0zSpJx3NIACFHAAAAl" +
        "VVVT6v//1qk+973wMYxjGLgAAJCXXOc5zpmta1rUKDAAF1BVgeM28lgAJGUxJPEp239/f3+IqKioqKiooMl//uvH9jGMZ9a1rWr8AAWTSYxjGMepSlKUfwACwPigEfnT4CPgZhfQyi3j" +
        "Dg/MABfjbMzNN////CN//sYzRrWtcL3ve96MAApK/e973vpznOc5oCoAAc1wzbdfl4P2pgAT92ZmZr////zjf/6KEi3ve+VjGMYxDAARF/gAAAMABH////+xvf/77sfzPT/rI76rI3HX" +
        "0TwANyUzMzQqqqqibP/6pEz61rXE973veggAU9Bc5znOia1rWtUkoAAZ0WCWebXYYTigAffqAAAI1VVVDl//vrQPGMYyq1rWtZcABObCEIQhCYxjGMYbXAACDP4KPXcvxesAC3+eZmaZ" +
        "///+fP/+GzUoQhCMc5znOSgALfNbWta1uYQhCEE+uAAaE7EurJJfengANKZGZmdSqqqkVf/71NS3SExxxyg444444YgAnuL5AnAmrVq1bBcuXLly5YUAN0SAGr2gAAB3///86f/9slMJ" +
        "gEXXXXxZZZZZZHgAZJyQ1wLN27du3ocOHDhw4XQD5iKAFtGZmZn////9mP/5/vH2ta2Fve973OgA2Z9CEIQhQYxjGMEjn/+xxcSdvp00zdLEY2X6oUANR/5mZqH///6v//wu5i1rWud7" +
        "3ve8oACd0zWta1sAQhCEHWNgAEDu6lx8euUoANA4mZmdP///7T//5nirBqVdddellllllk0ABd4zYNE7nDhw4c9mzZs2bNUdmpyAAP3bzMzRP///04AXXFMmJqYa9AcepnqZ6mepnr3a" +
        "92vdr3a92vdr3avL//+m4NMs0h6gBF5ReUXlF5RgbYG2BtgbYG2BtgbXl9neB2YANT5mZmdVVVVRO//sVCwlKUqpCEIQfWAD5+KlKUpTm973vdTDH83AD928zM0T///+3//5xpLve98T" +
        "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" +
        "MYxjGLgAAJCXXOc5zpmta1rUKDAAF1BVgeM28lgAJGUxJPEp239/f3+IqKioqKiooMl//uvH9jGMZ9a1rWr8AAWTSYxjGMepSlKUfwACwPigEfnT4CPgZhfQyi3jDg/MABfjbMzNN///" +
        "/CN//sYzRrWtcL3ve96MAApK/e973vpznOc5oCoAAc1wzbdfl4P2pgAT92ZmZr////zjf/6KEi3ve+VjGMYxDAARF/gAAAMABH////+xvf/77sfzPT/rI76rI3HX0TwANyUzMzQqqqqi" +
        "bP/6pEz61rXE973veggAU9Bc5znOia1rWtUkoAAZ0WCWebXYYTigAffqAAAI1VVVDl//vrQPGMYyq1rWtZcABObCEIQhCYxjGMYbXAACDP4KPXcvxesAC3+eZmaZ///+fP/+GzUoQhCM" +
        "c5znOSgALfNbWta1uYQhCEE+uAAaE7EurJJfengANKZGZmdSqqqkVf/71NS3SExxxyg444444YgAnuL5AnAmrVq1bBcuXLly5YUAN0SAGr2gAAB3///86f/9slMJgEXXXXxZZZZZZHgA" +
        "ZJyQ1wLN27du3ocOHDhw4XQD5iKAFtGZmZn////9mP/5/vH2ta2Fve973OgA2Z9CEIQhQYxjGMEjn/+xxcSdvp00zdLEY2X6oUANR/5mZqH///6v//wu5i1rWud73ve8oACd0zWta1sA" +
        "QhCEHWNgAEDu6lx8euUoANA4mZmdP///7T//5nirBqVdddellllllk0ABd4zYNE7nDhw4c9mzZs2bNUdmpyAAP3bzMzRP///04AXXFMmJqYa9AcepnqZ6mepnr3a92vdr3a92vdr3avL" +
        "//+m4NMs0h6gBF5ReUXlF5RgbYG2BtgbYG2BtgbXl9neB2YANT5mZmdVVVVRO//sVCwlKUqpCEIQfWAD5+KlKUpTm973vdTDH83AD928zM0T///+3//5xpLve98TGMYxikABUjJOc5zn" +
        "kta1rWJ7DC/4QAZuwMzM6aqqqj5//VmqoIQhE+c5znK0AJKNRCEIQjcYxjGIeHuYsAKEoTMzPn///9d//u+KB73vgIxjGMXUAD4/bWta1sYQhCEGpgrAONgBvt4AAAfVVVU73/85SqAh" +
        "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" +
        "8ra1rXm973vd2ADvMDGMYxkFKUpSjkGkSiAHMLZmZoaqqqpw//wcG9Wta2vve972aAEePd73ve/HOc5zlUILtMAKEoTMzPn///+t//qRgqMYxklrWtaz0AGNA2c5znQFa1rWqxLkEIAM" +
        "NiszM2n///+Z//lz5W1rWvN73ve7sAHeYGMYxjIKUpSlHUOAAAAWygIB0BFXhDGC1BbQ+QbkCiyFsry9EuOTL//3/////4Wj/8Py2z//wFVVABXv/9A//8Bud/yMn/uHx/w9CqXaT/b8" +
        "FUqbP7Yi/8IP9f1qoA/9M7/8lv+pc/+wv9tw/+BP7scVRq/3mz/5B/u9tVHp/bkNUmniH94Nf9Lrf///uvAAAiz9m0JfRxxxz///////ZbDvWACXLuSn9UooooySSSSSSMcbBoAb2Xr2" +
        "hMOOOOP//////+wYSgBPkfGbxHffffkkkkkkkhfbAD+n9HydLLLLLbbbbbbbQWNAEqY0oIIMGGGGZJJJJJI8ugAY3ZuArWV1113bbbbbba0GAQyMj223FJJJKNttttttVyAObA1kO5wg" +
        "gghbbbbbba37gE6TUcnSAyyyzG222222wXAH9P6Pk6WWWWW2222222rIAM7h2pT+0UUUVJJJJJJIkAAWoLPoS97jjjk//////+/AArRCgsstRhhhkSSSSSSQWABi9x4ZvFt99+ESSSSS" +
        "SN2ADC8ly9oVjjjjv//////3dzgAAFtJuMYxjHs5znOajABIvMta1rXApSlKULQARBmQAAADABH////+PgBMZhUpSlKo1rWtaWIAkPyd73vfHhCEIQSEANPdwAAAAwA3////+ogBRCVC" +
        "EIQhXe973u9gAnyTMYxjGW5znOcpYAUoA61rWtgSlKUpKEAKSDfOc5zp8YxjGIJAF43jWta1s6UpSlH5ADoXpCEIQh/e973t/ACX0jjGMYyLOc5zmPwBjlj3ve98SEIQhBbABFzva1rW" +
        "ttSlKUo1wAffK3ve98AEIQhB6DylrR//8BVVRhP59BqlT/1Dqqhh/p/X/wf/ISqqA/98r/+kf6vwqng/whL/29/RXn/i397Mf+4f5YN/8h/qfNVJ3+63KqKP8QQVTjfzGUqdQz/k5FUz" +
        "CJAAKwSAODY3NNNN5JJJJJIqQA8rBUGxc6aaaeSSSSSSPkAFrCd5WsGLrrsC222222mkAjD4GoopGeeegttttttsJADixlb77+8889LbbbbbbRQAX3vuuuvDDDDEkkkkkkjgACgodLyd" +
        "J7LLLVbbbbbbUOARWALc89Ltttu0kkkkkjywB71LmaacDzzz7ttttttoKAN1N5/k6bWWWXO222222PIBlc62mmn8889DbbbbbbW4ALt8rhcG7aaabKSSSSSRwoBYhY/9SpIooouSSSSS" +
        "SOUAKmVcVP6uRRRR8kkkkkkGIBRztk0Jf5xxx2//////8VAGR1DdSoCooopCSSSSSSBynAAczVWta1reUpSlKIwAcX/wAAADAB3////9EgHCF/nOc51eMYxjDSgHL22lKUpUeta1rTvg" +
        "HQvSEIQhD+973vb+AHgXDnOc51mMYxjDGAH7sqUpSlMa1rWtXkAIe2JznOc8DGMYxfmAJJziEIQhEe973vbMAJ6n05znOfhjGMYtdAKxTCUpSlNK1rWtWZALvyIhCEITnve97y5AM95A" +
        "QhCELr3ve92hAOXttKUpSo9a1rWnfAQK7ClKUpW2ta1rQCALC3yEIQhE+973va3prSgrLvAbgABvVOYAAGD/nwAAVv+pAABP/7AAAEp/tYAAR9UNgABFqmUAAEUqZYAARVUQAABFf7qA" +
        "AEbVDoAASKpiAABLql8AAE5VBwAAUlUlgAADt/zjQABqbob3NicA3SyOCjgo4KOCjnS50udLnS50udLnS4LgDv2J/sMeu51ZNHTR00dNHTSD0g9IPSD0g9IPSD0dQAIjphfSx1G1HyFa" +
        "RWkVpFaRXGlxpcaXGlxpcaXGluG//7E1h0LgkSJEi40aNGjRolEAteIj8PswoB/T8+/Pvz78+/Sl0pdKXSl0pdKXSl0Wn//l3Ias8Ro0aNJ169evXr1lX//EGAnJmnz58+myZMmTJkxq" +
        "//9zbVE4vVq1atoYMGDBgwWe//621EB4G/fv38nbt27du3Xt//z9mZxVBYsWLHDx48ePHjob//j7ES4ivHjx5AAgQIECBAKf/++N0HLv48ePHsECBAgQIDRf/9jazCQSHDhw4v79+/fv" +
        "36I//6Mzqq96RIkSJzRo0aNGjNR//yRiz/j8GDBgyGzZs2bNmcn//r/Vctbdu3bt5cuXLly5b8hgAMoA5ftttttvVVVVVVUGADtJ/Tnzzzz0SOOOOON28BGzN0nzzzzz8444444x8AVC" +
        "sKfhhhhhkOOOOOOMKAGPDlLgYYYYZLjjjjji8YB1F7yGwwwww/xxxxxxmSAjB18YnnnnnsHHHHHHDrAKbZ1w8MMMMNhxxxxxwwQDFDWzk88889WOOOOON64A6LM7IGGGGGNjjjjjjd2A" +
        "RVCfOqqqqqs4444443MAFJvbOlttttuX//////foBhyJd755556GccccccS2Act1fowwwww0ccccccZoAIklAIgwwwwx3HHHHHGRQBrr4mo22223EqqqqqqqmgvV8//18AAbz3MzM6//" +
        "//6FHjgAtov5l//9f/kAxwATfbMzM4qqqqdo/ADU+KMxVVJqozn///xMALPc1l6UpSrCEIQhCVwsIF56rDlzfXVQy0wvoOBZO864L60UqzHBvUQ0/+4gwMYxkNrWta0qKeP/+kjB8Uv7" +
        "bbbpttttttlL//++942RmWZh+H4pincr3K9yvlR5UeUjp//6DjpXG9JbwVBU/0GxaMWjFpjr46+MaP//wGJo3eg5TeAi8IvCLwi8JDmQ5kOZDmQ5kOZDmN3I9TX/+swAJr6MzM16qqqp" +
        "6pDwAvigAAANVVVU0C1ACSa5mZnCqqqpJUgAyZDMzNBVVVU9QV//ew7TOc515SlKUnqP/7Z7iLWtbJ3ve97sr//KrqcIQhFuc5znM3//0ucta1rXe973veXWgDFCUYxjGNMpSlKTVACz" +
        "4Z73ve+rOc5zmBgCt/+lKUpTS973vdzgCNpoIQhCEtjGMYxLTyT/kABhdIAAAbVVVUS1gB9gMzMzv///+ttAHmzMzM1VVVVRT0AWlMAAAGVVVVMc3/+4IHBjGMdta1rWsX//svWYQhCF" +
        "c5znOcrf/6oIrpSlKsQhCEIGj/+2e4i1rWyd73ve6r4Ay+Xve97385znOckAA7jU973vfNnOc5zNwBHEf3ve98Kc5znN+gBAss5znOdK1rWtajF3vPUU92p/9fzMzNWzMzMPf/oAAAMA" +
        "BVVVVT9f/HJmZmmO7u7bj/5fmZmbC7u7sKYAkCZjpcHyqqqs9999998bAFX+LxYVPqqqrAooooooZ4Ay00YA2AZVVVYlFFFFFC5gGEtTOLxiKqqrDffffffbuH7AAERVEQAADhVHQAAE" +
        "3/zSAAD7/5Jv//RudmZDgwYMGI2bNmzZs14P/+SsXkBwbNmzZ6nTp06dOmA//793bFjosWLFjR48ePHjxz4//4ewAo3xUqVKmPPnz58+fDBAD8gR96SSSSSyqqqqqqR4BL/7Xlzzzzz9" +
        "jjjjjjcGAWlth2jDDDDF8cccccaOgFVn6MO22223iqqqqqqf+xAYACw2GZmaX///+wwBALYAAAR////lIAe5oAAAIqqqqjtAGPVAAABv///+XL//DKoPve+ADGMYxit//ty/sAAABP//" +
        "///3//6mQbGMYx61rWtauL/+snhqlKUvBCEIQeWgBwPfWta1r0QhCEHggCE0/GMYxjpKUpSkRgCeUK973vfnnOc5yjgCa4lrWta2UIQhCDIUbAAERVEQAAKSqBgAAb1USAAAqKpuwAvO" +
        "D98GdQCL6XLoy6MujLoy9JvSb0m9JvSb0m9JujwBvrap2xz6PVD75/+f/n/5/+m+pvqb6m+pvqb6m+oBQEI1IXqIcmdMSf0D9A/QP0D9RjUY1GNRjUY1GNRjQTAIPqV/UbdNloqppqaa" +
        "mmppqa7mu5rua7mu5rua7mqXwBihrZyeeeeerHHHHHG9cAdFmdkDDDDDGxxxxxxu7AIqhPnVVVVVWccccccbmACMHXxieeeeewccccccZy3/wAtozMzM/////xwAe5oAAAMCKqqqoQAF" +
        "8UAAABqqqqnpwBpiWZmaD////u///pc5a1rWu973ve9w//5XVkEIQhfOc5znLf/+CGrQhCEQ5znOc3h//f8FpKUpUyEIQhBTIA8SYYxjGMopSlKUPgBIS65znOdM1rWtaWwBWBQEIQhC" +
        "axjGMYMwBWQ73ve98Oc5znONTUnjgA+CdmZmq////VfgCYH0/+qqiVT5XgDclMzM0KqqqogsAe5lw/uqo+VGzf/+Jntc5znWlKUpSkb//ciHpjGMbNa1rWtL//1bi6lKUqBCEIQg2X/9" +
        "RuBIxjGYWta1rTGAF+TyEIQhDoxjGMWOAHD6LGMYxk9KUpSiJAIZMXve97585znOZUAI2mghCEIS2MYxjEfFNGmnDQ81lH/ydmZmcm7u7tlVtgBHVFH4a+NVVVZ4444442yX+l/72MzM" +
        "0H3d3dTHUAJIjY0Gk2AAAAlVVVVVU8RDX/63MzM0V3d3aKwABNivC/kPAAADABPPPPPPOaDNf/CWZmZ0GZmY54AAuedbb17CqqqtooooooohVdIA7qVmZmqVVVVAQA82ZmZmqqqqqWIA" +
        "82ZmZmqqqqqM8Ac/7MzM7VVVVUX//2QKJ73vfoxjGMYtH/9HYmHve+IjGMYxht//JoBIYxjJ7Wta1qw//wrJBve987GMYxi7gAu7k73ve+IOc5zm6YA3YsQhCEIfGMYxiugBBrcIQhCE" +
        "ZjGMYxHABKAtjGMYx2lKUpSRasDDf//jpmZmf5mZmTF/7zzMzNuzMzL1//YQAAAI1VVVLb/6WzMzODd3d2C4BwVmnnk51VVVcggggggbsAQpty5jECqqqruuuuuuuBACekKDP7vFVVVf" +
        "ddddddWqAWjfOQVdmAAABcMMMMMMHdxj/sGqqcz/NBFUxX9/LKpaf6P7VRu/vpeqhX/Nw1U3H9sRf+t/4yaqmr/pXlVJz+1JFUwv8CU/+P/x0v/5P/PQVU+v9V4/+1/2bNVMS/w5EABb" +
        "gC3gi7PPRrbbbnJJJJJJA6AbCpkb78S888+rbbbbbaNwD+n9HydLLLLLbbbbbbbQAAmKzUYuD5NNNPpJJJJJHbgFrCd5WsGLrrsC222222mkA1dg5UqACKKKNkkkkkkiLAH3R+T5Oqll" +
        "lmFttttttXIBLl3JT+qUUUUZJJJJJJFwALOHr4dzSBBBBu222222WoBqLSTL2imOOOU//////7PAPioEwbF9pppqpJJJJJIcgCVsPTtCZscccl//////5SAWQTf5kPMgggh7bbbbbabg" +
        "DR3boK1i9dddq222222tcAe5URpe0jxxxzv/////+nQEec8www0kkkk22222222r4Bqa9jGMYx/Oc5zmigB9db73ve+dCEIQgOQCV3lWta1r+UpSlJWwCyLrKUpSl5a1rWsRQDR6dznO" +
        "c6rGMYxhvgD3A7e973wgQhCEGqgEozta1rWupSlKUnYAFgdZ73ve/ZCEIQewgGg5eUpSlMC1rWtYJgHoK8YxjGOZznOc2qAJLwgQhCEKr3ve94FAK7D5SlKUsLWta1m2AM4CFrWta5FK" +
        "UpSf2APKe+MYxjKM5znOWpASLzLWta1wKUpSlC0AU3osYxjGR5znOcxWMH/nwH/1n+n9f/c/61uqom/sqeqjv+0ZVU6v7WjqpE/tiL/5/+1Y//n/7RlVT3/smf/6P+va//m/61uqpI/q" +
        "jL/5z+mOFU8/6B//6V/2rIAArwBzoBMWWX4www4JJJJJJD9AROC7itYRXXXYdttttts8QCkHQJRRTc889HbbbbbbUEAYPnedddyGGGHpJJJJJIDgDlQuaIzhL778KkkkkkkaSAiEOHWs" +
        "Bzrrrt2222221VAFEP4VawJuuuvDbbbbbbM4AwHXNMMM1JJJMNtttttrjgHEjg4l7WnHHHj//////rsBDYsvP6mJRRRVkkkkkkhjAKCh0vJ0nssstVttttttQ4Be/J7R8oiyyy422222" +
        "2ymAODJF1gLBdddeW222222OgCFR6fk6RWWWWe222222w4AT1JJp0fassst9ttttts0wC3gi7PPRrbbbnJJJJJJJBYBHReEIQhCW973veggBUsppSlKU/rWta1IYBkUbMYxjGm5znOcQ" +
        "YB1pjKUpSlpa1rWsqQCNFNe973wEQhCEHWQCn7kpSlKVVrWta0mADGMopSlKVdrWta0ZgDp1XrWta2pKUpSjpwEXO9rWta21KUpSjXAFL9M5znOeRjGMYvaAGJd0AAADAAZ/////XYBz" +
        "c9MYxjGq5znOcKICKJwMYxjGw5znOcAwCkg3znOc6fGMYxiCQDCSsQhCEJD3ve96ngDhC/znOc6vGMYxi5CCP+xaVVBP6sxqpQ/pTmqk7+dw6qRP5ZNqpC/jNpVOv+AqqqQf3BAqot/X" +
        "1dVMP9N7qqD/zkKqn7/IilVJ/8FT//R/t+CqnP+tbqpgv9txAAKuAc2BrIdzhBBBC2222221kAETgu4rWEV112HbbbbbbPEApB0CUUU3PPPR2222221BAGD53nXXchhhh6SSSSSSA4A5" +
        "ULmiM4S++/CpJJJJJGkgIhDh1rAc6667dttttttVQBRD+FWsCbrrrw222222zOAMB1zTDDNSSSTDbbbbba44BxI4OJe1pxxx4//////67AQ2LLz+piUUUVZJJJJJIYwCgodLydJ7LLLV" +
        "bbbbbbUOAXvye0fKIsssuNtttttspgDgyRdYCwXXXXlttttttjoAhUen5OkVlllnttttttsOAE9SSadH2rLLLfbbbbbbNMAuIDOoS+Pjjjmf//////+SASXhAhCEIVXve97wKAV2HylK" +
        "UpYWta1rNsAZwELWta1yKUpSk/sAeU98YxjGUZznOctSAkXmWta1rgUpSlKFoArNACEIQhNe973vNIAzIssYxjGZ5znOcksA77EkIQhCT973velMBIAVYxjGMrznOc5V4BVrIve974GI" +
        "QhCDk4BlDShCEIQ7ve973FwB3HRznOc6TGMYxiVACOi8IQhCEt73ve9BACpZTSlKUp/Wta1qQwDIo2YxjGNNznOc4gwDo24lKUpTita1rVP8C/48XVT//fq1VP39pSf/lf1Fp/97/OAv" +
        "/2n8as1Ut/vVlVSf+yZ//yv6W4//B/lwyqmD+GYv/tf3BAqpP/VuKqj385if/efxi1/zV/lQ9VXtAFfGmB/UuqKKKgkkkkkkU0Az2EqffgLnnnq7bbbbbZGgHnXefaE14445X//////q" +
        "IBJGuLHHIFVVVZ//////8uAK2P8JvEi999+qSSSSSRzIBmsfVtCZEccci//////7ZAPGxtEkkxbbbcZJJJJJIdwCQOIXqVBlFFFOSSSSSSL0AVgkAcGxuaaabySSSSSRUgDLZqs2LjjT" +
        "TTgSSSSSSJKAd5qSXiNQ++/A5JJJJJHzQEec8www0kkkk2222222qAAqhTBPPPzbbbeJJJJJJHPAGS0tckkp7bbb1JJJJJI3EA7TflGGG4kkkoW222220MgIs/ZtCX0cccc////////u" +
        "gDiBe5znOd1jGMYwFwENeGta1rX0pSlKS6AFAPXve975EIQhCCEAF7zApSlKWNrWtazOgHAJBa1rWvRSlKUmZgITIUpSlKW1rWtayQAJ6n05znOfhjGMYtdALvyIhCEITnve97y5AN2h" +
        "L3ve9/iEIQg8WAQecwhCEIU3ve97w8ATln4QhCENb3ve9zyAXP8fe9731oQhCD+qAbdamMYxjNc5znORkAgdp973vfGhCEIQToAmz0GMYxjQc5znOM4AthZ4xjGMkznOc54EAAACMQAB" +
        "4CS/hgtwc91bNF8F75G88Suif/7///CgABR/+H5baMAACaqhAr3AAAaVTwAAAwAAWv+ob///914AABdVb///xWwn+wAPVWbbbXDMkcAFwAaSSQmEIAP1Wdtto0OHAAAoWo93d4UIQhCD" +
        "Jz4ASt+Lu7vU5znOceJQArwdAAADAOta1rWhYAChaj3d3hQhCEIOcktA+uj7gAANbbbczMzMwDoAAcpAAC7MzMsrUAEqySSwzMzL2ZQAJBbbcWZmZkX40AA8X+ZmZ3VVVU0xAGmJZmZo" +
        "P///8y4BDgWZmZ5VVVU8QAWOXmZmf1VVVHiCABfJ4dVVVVVVhhhhhhhY4wDhACKsccccc5RRRRRQ9hAH7fXxxxxxxyyyyyyyyAAAbIkWXVVVVVYzzzzzzz3PUWwR0QH7F1wAAMf/V1AA" +
        "BR/8AoAAPlUtIAAB+qj4Af//SQfowwjjjjpo0aNGjRnSP//qUSUkaV1114uXLly5ctpT//074sP8yKKKLoUKFChQn9L//51jBBlS6667vXr169evKUUAGFkknRttgMAI62260kkQwAMI" +
        "kkp9ttSwARcAAHv//ga92/gDrgnmX/0VVv//+mH/qqrbbbYR3/QVXCSSNuL/zKrH//+UJv/PySS4220pv+2Eklfttqi/97JJKzbbVg/8lbbeKSSIMh//VamMYxjNc5znOU+//rRoxjGM" +
        "cTnOc5v0//1fu973vgwhCEIOiP/7DjOc5zoEYxjGMRULiAAADFcCAdAZ9fUQwYLUKgMPtLgx7tZIsfR2v/9/////+Fo//D8ts//8BVVQAV7//QP//C8y/81D/74t///3XgAARZ+zaEvo" +
        "4445///////sth3rABLl3JT+qUUUUZJJJJJJGONg0AN7L17QmHHHHH///////b9sALz6ndHynLLLL7bbbbba64uyAAFtJuMYxjHs5znOant4AlltOc5znYYxjGMKZYAP3ZUpSlKY1rWt" +
        "axIgAw0S1rWtbClKUpSlwEFz//4CqqgPGH9oS1SXIf8PQqmvp/4aiqhF0gAE7S+b5OoFlll5ttttttiyQBuZ0wIIJmGGGNJJJJJJFr4A5sDWQ7nCCCCFttttttsTYA/p/R8nSyyyy222" +
        "22220NIwAMNEta1rWwpSlKUl4oBgK3CEIQhbe973vFmAMsUQhCEIT3ve97ygACyq6ta1rXcpSlKUlZHYEsrf88BqnNV/jxdU42f4Ql/6UT+nNv+X6AAk7O1SSSxtttwEkkkkki6gA/KK" +
        "STo/JZZZdbbbbbbYmAB9UqlVVXAAAAMB//////+mABFIGN/Uq6KKKOkkkkkkh5rAB98re973wAQhCEHcgAiLAYxjGMhznOc5lgAMBW4QhCELb3ve926AEMfnnOc50uMYxjGPZvZ/qHVV" +
        "Cm/zYOqcAf3BAql+/8lJVRo2AFqCz6Eve4445P//////vwASJuS3iNG++/AJJJJJJIVAA/KKSTo/JZZZdbbbbbbYmADJ6Lt7QonHHHT//////42cARNUFKUpSpta1rWmUAILUb3ve9+C" +
        "EIQg+sAEHnMIQhCFN73ve8PABwxHrWta2JKUpSkyEA9ydYJfv8AABJ/7YAABXqlMAABzqnPAABKVR9i//8c4dCqBUqVKlvPnz58+fIU//8O3/QWnv379/d27du3btvg//7q2yqbl+/fv" +
        "4e3bt27duui//7Y2MXeFGjRo0/Xr169evNFr//+KOcS4QyYAAs3LNyzcs3LQ70O9DvQ70O9DvQ7zsz//9uGbaQNJ3hckeJHiR4keJLbS20ttLbS20ttLbSNz//8/OZ74HGWugWfFnxZ8" +
        "WfFs6bOmzps6bOmzps6aU3//8ig5CEjZAPTd63et3rd63kn5J+Sfkn5J+Sfkn4Ry1GQmADzZmZmaqqqqozwBJNczMzhVVVUycAe5oAAAIqqqqjWACFHAAAAlVVVUZj//5bGFKUpTCEIQ" +
        "hByf/+6ebta1rbe973vcQ//zuj6xjGNOta1rWLf/+CGrQhCEQ5znOczTYBIimta1rXAhCEIQCAA25UpSlKU3ve973YAApK/e973vpznOc5iAAcTjpSlKUwve973lJ3HwbqrALyo3v9rV" +
        "apJI/L1Fx//+wuXqU3EqVKlS0iRIkSJEgfVctP6rWcWi5/tlV/bbaTwFH//zMhfBCEFChQoV9+/fv379r9Z1/ipvEV1/zFVy220yMIf/+5ocMZtShQoUKO/fv379+7Ywp/rZfHg//Kqs" +
        "kkkcBP///luSoRs3Hjx48h8+fPnz57cJf/aAQtLkP9aAF7bbA+g//6zwW8LM7FixYtFKlSpUqU3PD/jwDch/zlVxttrwNP//KVUsSy8gQIECImTJkyZMjMr/1QAbZ/w6rNJJGz9f/+AL" +
        "e9acZIkSJE0WLFixYsO5B/L4EG/9lVa222fj///az9E7w2JEiRIpixYsWLFgrd/qJf/FDb//A4AOQwAAAwA/////KLs8Ac/5GmJVTvqY4LgAaYlmZmg////ukIAQZpNsp//Gf4Zn/4n2" +
        "7qETpFWLd9Q0J/9mqwfRtNq5T//fVnEUqF5Ik7lc0JUd//1FAq0npMf4MnxLaCooACMNDrVt2uNh/Tr80fbi9nlOgtXgAF4pvNpXeUNyxQx2a1pqAEnMg0hD5VbSO3N5fxSzZIfgAT92" +
        "ZmZr////2RALlnAAADP///6ZAHmzMzM1VVVVUFgB15ZmZm6qqqqcX//unm7Wta23ve973KP/8lF4HOc6CpSlKUh9//U5ka973ycYxjGLD//3ExZSlKVQhCEIQYOAPgCwhCEIUmMYxi/k" +
        "AM5ZUIQhCHhjGMYsCAK763Oc5zqGta1rSCAJd0BjGMY0ClKUpRaMiRIAlUXMzM9qqqqYqAa+nmZmhKqqqdsAU6szMzSqqqqnHAFSemZmbFVVVU+T/+6PZpCEIVTnOc5xt//wnYgpSlLY" +
        "QhCEH1f/8n6Q73vfMxjGMYsb//NgDQxjGP2ta1rWIwBk+AQhCEIrGMYxibABdIGc5znO1a1rWszABW//SlKUppe973u5wBSlqxjGMZHSlKUo/eeQ2rIWH/Z//xAAAAMA1VVVSoAA3TIU" +
        "flopVVVY3XXXXXWXL/9/mZmaC7u7tfAAr3cdfPeOqqqtd999999Ox/9qmZmaHmZmX6gBCm3LmMQKqqqu6666665zD/64ZmZniZmZitAC551tvXsKqqq2iiiiiii/S4R/ovyqen/gKqqN" +
        "b/Kh6p5n+l+KqeYA/KKSTo/JZZZdbbbbbbYmAHpWyd9+Eeeee9ttttttXwA67n1XXYJhhhlSSSSSSPpAHjY2iSSYtttuMkkkkkkhQAKxTCUpSlNK1rWtWZAKrcHve976kIQhB/uAKpfJ" +
        "jGMY0XOc5zi1ALKrq1rWtdylKUpRMUI+wAcwtmZmhqqqqdIAXabmZmgKqqqfGAU6szMzSqqqqg6AGmJZmZoQAAADADp//aiWEQhCGs5znOcR//2QKJ73vfoxjGMYm3/9e30+c5zySlKU" +
        "pQP//UyDYxjGPWta1rWfgCJPTnOc50LWta1qcACRklCEIQhoYxjGLagCb3U5znOdi1rWtZzACsh3ve974c5znOcynHZ//OgAAAMCv///8m/+VgAAAwF6qqqjd/8ZAAADAM1VVVGn/35m" +
        "ZmbZmZmX4wCjCd/urXVVVVfvvvvvvowAWXjuZuJQAAADAW222222ngAxdVZFrAiqqqt000000ytAHBWaeeTnVVVVyCCCCCB3If/ppmZmekREQ+n/82ZmZnGZmZlmf/jpmZmf5mZmSv/7" +
        "6ZmZnTu7u64QBWPc8JkZtVVVa111111yJAMNZGZPUhAAAAx555555YYBvGEomJc4AAAHGGGGGGBVAQR2zMUbWqqqrtNNNNNM1k//FzMzNAIiIh6T/3qAAAB2qqqop/+y5mZmqu7u7dn/" +
        "0fMzM1wiIiGCwEBe01bRwKqqq7HHHHHG8EAmBpiFU2IAAAMAm222222cwBZOU5zZfNVVVbCCCCCB+NANNBFnLrrqqqrgooooooWrf/MQAAALf///z//4YzMzOfd3d1p/+5MzMzciIiIS" +
        "f/1WZmZoxEREOzQDwFZDHRawAAAPVVVVVVLCAjqij8NfGqqqs8ccccca6gFSAb2ErEqqqrAQQQQQQBwAyk6YNW6mqqqt5ZZZZZXWPA//MZmZmlEREQ4b/4XmZmbS7u7tKf+4JmZmpkRE" +
        "Q0f/1KZmZozu7u5agD5h07ru21VVVlRRRRRRGkAlZ4iknHpVVVXuOOOOOMngFj5rn/plqqqrBZZZZZZKIA1T4WDs6UAAADZ55555372n/7EZmZnfu7u6nf/RDMzM9oiIh93/5BmZmbJm" +
        "ZmYDf+88zMzbszMzFN///Bf+CN38W8atirYq2KtiraO2jto7aO2jto7aO2Zr//9tH7YaNEPhLkn5J+Sfkn5NpTaU2lNpTaU2lNpTDP//6h/0/nS4aFiYomKJiiYomS5kuZLmS5kuZLmS" +
        "5j4f//yy/lbaTEhuSGchnIZyGch8QfEHxB8QfEHxB8QaZwf/0czMzPXd3d02/+R5mZmyEREQq3/vwzMzQaIiIef/9kmZmaI7u7ubkA7JrRdniaqqquccccccZ6AIyrpFT04VVVV5RRRR" +
        "RQ5IBTwY+GtfyqqqwBBBBBA9bAMjQ2ICEeqqqrd99999957tqYAqEnMzM+////igArvGZmZyqqqqMAAoK6ZmZxqqqqX8APc0AAAEVVVVUjgHhu79do1yyNZ+4XuF7he4XukDpA6QOkDp" +
        "A6QOkDhkASDXE6tlqtUTZ7uO7ju47uO8r/K/yv8r/K/yv8r+5IAq+ek87zzZuV1ixYsWLFixYvCLwi8IvCLwi8IvCLGABnMWEoo6hwABFZ0WdFnRZ0Wjio4qOKjio4qOKjioSEAukDOc" +
        "5znata1rWZgA25UpSlKU3ve973YABA8Yta1rW+hCEIQFQBPKFe973vzznOc554A=");
}
