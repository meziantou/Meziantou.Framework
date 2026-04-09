using Meziantou.Framework.InlineSnapshotTesting;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Meziantou.Framework.Tests;

public class QRCodeTests
{
    // ───── Numeric mode ─────

    [Fact]
    public void Create_Numeric_1Digit()
    {
        var qr = QRCode.Create("7", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.###.#######
            #.....#...##..#.....#
            #.###.#.##.#..#.###.#
            #.###.#.##..#.#.###.#
            #.###.#.#..#..#.###.#
            #.....#..####.#.....#
            #######.#.#.#.#######
            ...........##........
            ####..#.######..###.#
            ...##...#..####..#...
            .#..######.#.....##.#
            ........####..######.
            ..#.#.#..#..#..#..###
            ........#.##..#..#.##
            #######...###..#.#...
            #.....#........##.#..
            #.###.#...#.#####...#
            #.###.#.##.#..######.
            #.###.#.##..#.##.....
            #.....#.#.#..#.#..###
            #######.###..#..#..#.
            """);
    }

    [Fact]
    public void Create_Numeric_2Digits()
    {
        var qr = QRCode.Create("42", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#####.#######
            #.....#.##.##.#.....#
            #.###.#..###..#.###.#
            #.###.#..#.##.#.###.#
            #.###.#.#...#.#.###.#
            #.....#.#.#...#.....#
            #######.#.#.#.#######
            ........####.........
            ###..##.#########..##
            ##.##..#.##..........
            #.##..#.#.#...#..####
            ##.#.#.#....#...#..#.
            #..##.#.......#..##.#
            ........#..#.###.###.
            #######....###.###.#.
            #.....#.####.###.##..
            #.###.#...###########
            #.###.#...#..........
            #.###.#.#.#...#...###
            #.....#.##..#...#..#.
            #######.#.#...#...###
            """);
    }

    [Fact]
    public void Create_Numeric_3Digits()
    {
        var qr = QRCode.Create("123", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#####.#######
            #.....#..#.#..#.....#
            #.###.#..##...#.###.#
            #.###.#..###..#.###.#
            #.###.#...#.#.#.###.#
            #.....#....##.#.....#
            #######.#.#.#.#######
            ........#####........
            ##.##.#..##.#.#.....#
            ###.##......#.#.#.##.
            ..##.####....##.###..
            #.####.#..##.....####
            ..###.#####...#..##.#
            ........#####..#.#.##
            #######...#.#####...#
            #.....#....###.###..#
            #.###.#.#.###.##.##.#
            #.###.#.#.###...###..
            #.###.#..##...#...###
            #.....#.#.#..##.#.#.#
            #######.##.#.....###.
            """);
    }

    [Fact]
    public void Create_Numeric_8Digits()
    {
        var qr = QRCode.Create("01234567", ErrorCorrectionLevel.M);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######...###.#######
            #.....#.###...#.....#
            #.###.#..##...#.###.#
            #.###.#..#.##.#.###.#
            #.###.#.##.##.#.###.#
            #.....#....#..#.....#
            #######.#.#.#.#######
            .....................
            #.#.#.#...#.#...#..#.
            ##.#....#.##.#.#...#.
            ...##.###.##.###.###.
            ##..##.#.#.###.##..#.
            ..#..###.###.###....#
            ........#.#...#....#.
            #######.....#...#...#
            #.....#...#...#..#.##
            #.###.#.###.#.#.###.#
            #.###.#..#.#.#.#.###.
            #.###.#.##.#.###..#.#
            #.....#....###.###...
            #######.#..#.###..#.#
            """);
    }

    [Fact]
    public void Create_Numeric_Long_Version3()
    {
        var qr = QRCode.Create(new string('1', 100), ErrorCorrectionLevel.L);

        Assert.Equal(3, qr.Version);
        Assert.Equal(29, qr.Size);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######..#..#..#.#.##.#######
            #.....#...####...##.#.#.....#
            #.###.#.###.##.#..#.#.#.###.#
            #.###.#..###...#..###.#.###.#
            #.###.#...#..#...###..#.###.#
            #.....#..#...##.#.#...#.....#
            #######.#.#.#.#.#.#.#.#######
            ........###.##...##.#........
            ###.#####.##.#.#..#.###...#..
            #...##..#.##...#..###.###.###
            #..##.####...#...###.#.###.##
            .##.....#..#.##.#.#..#.#.#.#.
            .####.##....#.###..#.###.###.
            .###.#.....##.#.##.#.##.###.#
            ########.######.##...#...#...
            ##.#.#.####.#.###...#.#...#..
            #.######...#...#.#.##.#.#.#.#
            ..#.#..#..##.#...##.#...#...#
            #.#.#.####...#.#..#.#..#...#.
            .#..##.#..##...#..###.###.###
            #.###.#..#..##...#########.##
            ........#..####.#.#.#...##.#.
            #######.#####.###..##.#.###.#
            #.....#.###.###.##.##...###..
            #.###.#.####..#.##.#######...
            #.###.#...##...##.....#...#..
            #.###.#.#.#..#.#.#..#.#.#.#.#
            #.....#.####.....##.#...#....
            #######.#.#.#..#..##...#....#
            """);
    }

    // ───── Alphanumeric mode ─────

    [Fact]
    public void Create_Alphanumeric_1Char()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######..#.##.#######
            #.....#..###..#.....#
            #.###.#.##.##.#.###.#
            #.###.#..#.#..#.###.#
            #.###.#...#.#.#.###.#
            #.....#.....#.#.....#
            #######.#.#.#.#######
            ........##.##........
            ###.########.##...#..
            .###...#..#...#...##.
            ...####...#.#...#...#
            .#...#.####...#...##.
            ###.###.##..#.#.#.###
            ........##.#.#.#.#...
            #######.#.##.###.##..
            #.....#.######.###.#.
            #.###.#.#..#.###.##.#
            #.###.#..#....#...##.
            #.###.#.###.#...#...#
            #.....#.#.#...#...###
            #######.###.#.#.#.#.#
            """);
    }

    [Fact]
    public void Create_Alphanumeric_3Chars()
    {
        var qr = QRCode.Create("ABC", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######..#.##.#######
            #.....#.##.#..#.....#
            #.###.#.##..#.#.###.#
            #.###.#..#.#..#.###.#
            #.###.#.#...#.#.###.#
            #.....#.#..##.#.....#
            #######.#.#.#.#######
            ........#####........
            ##.#..##.##...###.##.
            #...#...###...#..#...
            ##..#.##.#..##...#...
            #.#..#...#.#.......##
            ..##..#.....#.#.#.##.
            ........#.##...###.#.
            #######.##...#.#.##.#
            #.....#..#####.###...
            #.###.#...##..###.###
            #.###.#.#.##.....####
            #.###.#..##.#...#...#
            #.....#.##...##.#.#.#
            #######.#.###...###..
            """);
    }

    [Fact]
    public void Create_Alphanumeric_SpecialChars()
    {
        var qr = QRCode.Create("$%*+-./:", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#####.#######
            #.....#..#.#..#.....#
            #.###.#..##...#.###.#
            #.###.#..###..#.###.#
            #.###.#...#.#.#.###.#
            #.....#....##.#.....#
            #######.#.#.#.#######
            ........#####........
            ##.##.#..##.#.#.....#
            ..#.....#...#.#.##.#.
            .##.######...##.##.##
            #.#.##.#..##.....##.#
            ..##.#####....#..###.
            ........#.###....###.
            #######..#..######...
            #.....#....###..##..#
            #.###.#.##.##.###.###
            #.###.#.#####..##.#..
            #.###.#..#....#....##
            #.....#.#.#..###.#.#.
            #######.#..#.....#.#.
            """);
    }

    [Fact]
    public void Create_Alphanumeric_HelloWorld()
    {
        var qr = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.M);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######...#.#.#######
            #.....#.###...#.....#
            #.###.#...#.#.#.###.#
            #.###.#...#.#.#.###.#
            #.###.#.#.###.#.###.#
            #.....#..###..#.....#
            #######.#.#.#.#######
            .....................
            #.#.#.#..#..#...#..#.
            .####...#..#....#...#
            ...#######.#..#.##...
            ####.#.##..###.#.###.
            .#..####.#.#..###.#.#
            ........#.#...#...#.#
            #######.....#..#.##..
            #.....#..##...##.#...
            #.###.#.##..#.#######
            #.###.#...##.#.#...#.
            #.###.#.####.###.#..#
            #.....#....###...#.##
            #######.##.#.###....#
            """);
    }

    [Fact]
    public void Create_Alphanumeric_ECLevel_Q()
    {
        var qr = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.Q);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######....#..#######
            #.....#.##..#.#.....#
            #.###.#..#.##.#.###.#
            #.###.#.#####.#.###.#
            #.###.#.##.#..#.###.#
            #.....#..#..#.#.....#
            #######.#.#.#.#######
            ........##.##........
            .#.####.##..###.##.#.
            #.####.#....####.###.
            ..#.#.##...#..##.....
            #.##.#...#.##...##...
            ##.########.###.#####
            ........#...#..#.#...
            #######..##..##..####
            #.....#.#.#..#..#.###
            #.###.#.##.#..#...###
            #.###.#.#.###...#.#..
            #.###.#..#....#....##
            #.....#.###..###..##.
            #######..#.#.......#.
            """);
    }

    // ───── Byte mode ─────

    [Fact]
    public void Create_Byte_Lowercase()
    {
        var qr = QRCode.Create("hello world", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######..#.##.#######
            #.....#..###..#.....#
            #.###.#.##.##.#.###.#
            #.###.#..#.#..#.###.#
            #.###.#...#.#.#.###.#
            #.....#.....#.#.....#
            #######.#.#.#.#######
            ........##.##........
            ###.########.##...#..
            ...#.#.####...###..##
            ###.####.#..##.######
            .#..#..#.##.....#..#.
            ###.#.##..#.##.##....
            ........#..#.#..#.###
            #######.#..#...##.###
            #.....#.#####..#....#
            #.###.#.#.##....#....
            #.###.#..###..###.##.
            #.###.#.##..#.#.#.#.#
            #.....#.#.##....#..#.
            #######.##.##..#...##
            """);
    }

    [Fact]
    public void Create_Byte_Url()
    {
        var qr = QRCode.Create("https://example.com", ErrorCorrectionLevel.M);

        Assert.Equal(2, qr.Version);
        Assert.Equal(25, qr.Size);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######....###..#.#######
            #.....#...#..####.#.....#
            #.###.#.##.#..#...#.###.#
            #.###.#.#....###..#.###.#
            #.###.#.###..#..#.#.###.#
            #.....#.#..#..##..#.....#
            #######.#.#.#.#.#.#######
            ........#.....#.#........
            #.#####.....#.....#####..
            .#..##..#.##.#...#.#...#.
            #####.#.##...####..#.#.##
            ##.###..#.##.#.##.##....#
            .###..#....##.##.##.#.###
            #####...#.#.....#..#.#.#.
            #.....##..###..#..####.##
            #..#...#...#..#######...#
            #.#..##.####....#####.#..
            ........##..#####...##...
            #######......##.#.#.#.###
            #.....#.##..##..#...##.#.
            #.###.#.###.#.#######.#.#
            #.###.#.#......#.##.#####
            #.###.#.#####..#.....##.#
            #.....#....#..#.##.###..#
            #######.##.#.....########
            """);
    }

    [Fact]
    public void Create_Byte_Version7Transition_ErrorCorrectionQ()
    {
        var qr = QRCode.Create("fserwrjthwekjfghredjkgdrjkgfdjkhghfhjkdsghhjsdfghjsdfghkkkkkkkkkkkkkkkklkia", ErrorCorrectionLevel.Q);

        Assert.Equal(7, qr.Version);
        Assert.Equal(45, qr.Size);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######...##..##.#.##.#.#.#...###...#.#######
            #.....#.#...#..##.#....###..##.#.#.#..#.....#
            #.###.#...#.##.#..##....##.#.##..#.#..#.###.#
            #.###.#.#..#.#..###..##.#####.#.##.##.#.###.#
            #.###.#.#.#.##..#..######.##..###.###.#.###.#
            #.....#..#..##..##..#...##............#.....#
            #######.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#######
            ........#....##.#..##...#.#.#..#..###........
            .#.####.###.####..#######.#.#........##.##.#.
            #...#..##.#....#.#..#..#.###.###.########....
            ##..#.#...####...##....##..#.#...##..#...####
            .#.#.#..#.###.###....##...#...#.##..#...#.#.#
            #.#.#.#.####.#..#....#.##...###.##.#.#.###..#
            .#.##.........#.##.##..#..#####..####.##...#.
            ..#.####.#######..##.#.#####...#.##.######...
            ##.#...#....#.##.########.........###....##..
            .....##....#..#......#..##.....####..#...#.##
            ...##..#####...###..##.###.#..#..##.##.#.#..#
            #.#.#.###...###..#..#.##.#.#.#.#.#...#.#....#
            #####.....###..#.#.####.#....#.#..###.#.###..
            ##########.##.###..########......#.######..##
            #.###...###....#.#..#...##...#.#.##.#...###..
            #..##.#.#.###..######.#.#.#.#########.#.##.##
            #.#.#...#..#.###....#...#.....#.#..##...#.#..
            .#.######...#.###########...#...###.#####....
            #.###...#..#...#....###.##.#######..####.###.
            #..#.##..####.##.###.......#.....#..#....#...
            ###..#...#....#...##..####....#.#..#....####.
            .#....#.#.##.###.###..#...#..#.#.#.#...###...
            ###.#....#######.##.#.#.####..#.###.#####.#.#
            ..#...#...#..#.#..#..######.##...#..#.##.##.#
            ....#..#..##..#...###.#....##..#..##.#..#####
            ..#.###..##..#.#######......##......#.#....##
            ###.#..#...##.##...##...#...####.###...##....
            ....#.#.....#.#######..#.....#...#####.#.#.##
            .####....#...#......#..####...###.#.......###
            #..##.##.##.###.#########...##..#.#######...#
            ........####..#.#####...#.#.####....#...#..#.
            #######..###..#.##..#.#.####...##.#.#.#.##...
            #.....#.#...######.##...##..........#...###..
            #.###.#.#......##.#######.#....####.######...
            #.###.#.#.#..#.#.#.##......##.#.####.....#...
            #.###.#..#....####....#..##.##..#...##..#.#.#
            #.....#.#####.#.##.#....####..##..###.#######
            #######..#.#...#.#.#..####.####....##.###....
            """);
    }

    [Fact]
    public void Create_Byte_UTF8_Accented()
    {
        var qr = QRCode.Create("café", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.###.#######
            #.....#...##..#.....#
            #.###.#.##.#..#.###.#
            #.###.#.##..#.#.###.#
            #.###.#.#..#..#.###.#
            #.....#..####.#.....#
            #######.#.#.#.#######
            ...........##........
            ####..#.######..###.#
            ###.#......####...#.#
            #.#...#.#.##.......##
            .###.#.###.#..####..#
            #.#####...#.#..#.....
            ........####..#......
            #######....##..#..#..
            #.....#...#....###.#.
            #.###.#..##.######.##
            #.###.#.##.#..#.#..#.
            #.###.#.#...#.##.#...
            #.....#.###..#.#.##.#
            #######.#.#..#....#..
            """);
    }

    [Fact]
    public void Create_Byte_UTF8_Emoji()
    {
        var qr = QRCode.Create("\U0001F600", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.##..#.#######
            #.....#..#..#.#.....#
            #.###.#.#.#.#.#.###.#
            #.###.#.#..#..#.###.#
            #.###.#.###...#.###.#
            #.....#.......#.....#
            #######.#.#.#.#######
            .........##..........
            ####..#.#.#..#..###.#
            .#..#..#.#..#..#.#.#.
            ..#..#####..#####.#..
            #..##..#....##....##.
            ###.#.###.##.#..###..
            ........##...#.#.###.
            #######..#...##.#...#
            #.....#...#####......
            #.###.#..#.#..#..#.##
            #.###.#.###..#..#..#.
            #.###.#.#..#.#..#.#..
            #.....#.##.##.#.##..#
            #######.#####..#.....
            """);
    }

    [Fact]
    public void Create_Byte_Binary_WithSnapshot()
    {
        var qr = QRCode.Create(new byte[] { 0x00, 0xFF, 0x48, 0x65 }, ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#####.#######
            #.....#.##.##.#.....#
            #.###.#..###..#.###.#
            #.###.#..#.##.#.###.#
            #.###.#.#...#.#.###.#
            #.....#.#.#...#.....#
            #######.#.#.#.#######
            ........####.........
            ###..##.#########..##
            ###.##.#.##......####
            ...##.#.......#......
            ##..##...##.#...##...
            .######.##....#..####
            ........####.###..#..
            #######....###.######
            #.....#.#..#.###.#...
            #.###.#...########..#
            #.###.#...#......#...
            #.###.#.#.#...#...###
            #.....#.##..#...#....
            #######.#.....#..##.#
            """);
    }

    // ───── Kanji mode ─────

    [Fact]
    public void Create_Kanji_SingleCharacter()
    {
        var qr = QRCode.Create("漢", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.##..#.#######
            #.....#..#..#.#.....#
            #.###.#.#.#.#.#.###.#
            #.###.#.#..#..#.###.#
            #.###.#.###...#.###.#
            #.....#.......#.....#
            #######.#.#.#.#######
            .........##..........
            ####..#.#.#..#..###.#
            .##......#..#..#...##
            ###..##.....######.#.
            ###.#....##.##.......
            .#.##.#..#.#.#..#.#.#
            ........###..#.#..##.
            #######..##..##.#.##.
            #.....#....####..#.#.
            #.###.#..###..#..#..#
            #.###.#.#....#..#..#.
            #.###.#.#.##.#..###..
            #.....#.#..##.#.##..#
            #######.##.##..#.#.##
            """);
    }

    [Fact]
    public void Create_Kanji_MultipleCharacters()
    {
        var qr = QRCode.Create("漢字", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.##..#.#######
            #.....#..#..#.#.....#
            #.###.#.#.#.#.#.###.#
            #.###.#.#..#..#.###.#
            #.###.#.###...#.###.#
            #.....#.......#.....#
            #######.#.#.#.#######
            .........##..........
            ####..#.#.#..#..###.#
            ...###.#.##.#..#...##
            ..#..##.###.########.
            .##.##.##.#.##....#..
            #.#...#..#.#.#..#.#.#
            ........###..#.#...#.
            #######..#...##.#.##.
            #.....#....####.....#
            #.###.#..#.#..#..#..#
            #.###.#.#.#..#..#..#.
            #.###.#.####.#..#.#..
            #.....#.#####.#.##..#
            #######.#.###..#...##
            """);
    }

    [Fact]
    public void Create_Kanji_Katakana()
    {
        var qr = QRCode.Create("アイウ", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######..#.##.#######
            #.....#..###..#.....#
            #.###.#.##.##.#.###.#
            #.###.#..#.#..#.###.#
            #.###.#...#.#.#.###.#
            #.....#.....#.#.....#
            #######.#.#.#.#######
            ........##.##........
            ###.########.##...#..
            .###....##....#..###.
            ##.#.##.#.#.#...#.#.#
            .##....####...#....#.
            .#...###....#.#.#..#.
            ........#.##.#.#.#.#.
            #######.####.###..#.#
            #.....#.######.##.#.#
            #.###.#.####.###..#.#
            #.###.#..##...##.#.#.
            #.###.#.##..#...##..#
            #.....#.#.#...####.#.
            #######.##..#.#...#..
            """);
    }

    // ───── Higher versions / different sizes ─────

    [Fact]
    public void Create_Version4_ByteMode()
    {
        var qr = QRCode.Create(new string('x', 43), ErrorCorrectionLevel.M);

        Assert.Equal(4, qr.Version);
        Assert.Equal(33, qr.Size);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######..##.#.#.#..#####..#######
            #.....#...##...#..#..#..#.#.....#
            #.###.#.###.#.##.....##.#.#.###.#
            #.###.#.##.#......##.#.##.#.###.#
            #.###.#.##.#######..#.#...#.###.#
            #.....#.#..##.....##.#.##.#.....#
            #######.#.#.#.#.#.#.#.#.#.#######
            ........###.....#.####.#.........
            #.#####...#..##.##.##.##..#####..
            .......#.######.##.##.##.##....##
            #.###.##.....###.#....#.#.##..##.
            ....#...###..#.#.##.....#..####..
            .#..####.###..#.#..#######..##..#
            ...#...#...#.#.#.##.....###....##
            ##...###..##..###.#.##..#.##..##.
            ...###.###.....#..#..#..#..####..
            ..#...##.#.#.#.###..#.##.#..##..#
            ##...#.#.#..#.#.#..#####.##....##
            .######...#.#.##..#..#..#.##..##.
            ##..##.#.#.....#.....##.#..####..
            ...##.##..##..#...##.#.#.#..##..#
            ##.#.....####.####..#.##.##....##
            #.#########..##...##.#..#.##..##.
            #..###..#.#..#..#.####..#..######
            #.###.#...#.###.##.##.########..#
            ........#..#.#..##.##.###...#..##
            #######..#.#.###.#....###.#.#.##.
            #.....#.###.#..#.##....##...###..
            #.###.#.###.....#..####.######..#
            #.###.#.#..#..##.##.....##..#...#
            #.###.#.#...#####.#.##...##...#..
            #.....#..#.....#..#..#.#..##.##..
            #######.##....####..#.###..###.#.
            """);
    }

    [Fact]
    public void Create_Version7Plus_HasVersionInfo()
    {
        var qr = QRCode.Create(new string('x', 123), ErrorCorrectionLevel.M);

        Assert.Equal(8, qr.Version);
        Assert.Equal(49, qr.Size);
    }

    [Fact]
    public void Create_Numeric_LongString_Version6()
    {
        var qr = QRCode.Create(new string('0', 300), ErrorCorrectionLevel.L);

        Assert.Equal(6, qr.Version);
        Assert.Equal(41, qr.Size);
    }

    // ───── EC levels ─────

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.Q)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void Create_AllErrorCorrectionLevels_ProducesValidQRCode(ErrorCorrectionLevel level)
    {
        var qr = QRCode.Create("HELLO WORLD", level);

        Assert.True(qr.Size > 0);
        Assert.True(qr.Version >= 1);
    }

    [Fact]
    public void Create_ErrorCorrectionLevel_H()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.H);

        InlineSnapshot.Validate(RenderAsText(qr), """
            #######..#....#######
            #.....#.#.....#.....#
            #.###.#..#.##.#.###.#
            #.###.#...##..#.###.#
            #.###.#...#...#.###.#
            #.....#.#.#...#.....#
            #######.#.#.#.#######
            ........####.........
            ....####..#.#.##...#.
            ###.##.#..#.#..#.....
            ##.####..##..#.#####.
            ##.#....#..##...##..#
            #.#.####....###..####
            ........#.#.##....#.#
            #######.###..#...###.
            #.....#.#......#.#..#
            #.###.#.#.#.#..##.###
            #.###.#..###..##.#.##
            #.###.#...#########..
            #.....#..#.##.#.#.#.#
            #######..#.#.##...###
            """);
    }

    // ───── Version / size transitions ─────

    [Fact]
    public void Create_LongerData_ProducesHigherVersion()
    {
        var qrShort = QRCode.Create("A", ErrorCorrectionLevel.L);
        var qrLong = QRCode.Create(new string('A', 200), ErrorCorrectionLevel.L);

        Assert.True(qrLong.Version > qrShort.Version);
    }

    [Fact]
    public void Create_HigherECLevel_MayRequireHigherVersion()
    {
        var qrL = QRCode.Create(new string('A', 25), ErrorCorrectionLevel.L);
        var qrH = QRCode.Create(new string('A', 25), ErrorCorrectionLevel.H);

        Assert.True(qrH.Version >= qrL.Version);
    }

    public static TheoryData<int, int> NumericPayloadLengthForAllVersions => CreateNumericPayloadLengthForAllVersions();

    [Theory]
    [MemberData(nameof(NumericPayloadLengthForAllVersions))]
    public void Create_Numeric_HasOneQRCodePerVersion(int expectedVersion, int payloadLength)
    {
        var qr = QRCode.Create(new string('1', payloadLength), ErrorCorrectionLevel.L);

        Assert.Equal(expectedVersion, qr.Version);
        Assert.Equal(17 + (expectedVersion * 4), qr.Size);
    }

    // ───── Binary data coverage ─────

    [Fact]
    public void Create_Binary_LargerPayload()
    {
        var data = new byte[50];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 5);
        }

        var qr = QRCode.Create(data, ErrorCorrectionLevel.L);

        Assert.Equal(3, qr.Version);
        Assert.Equal(29, qr.Size);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#...#.#..####.#######
            #.....#..####..##.#...#.....#
            #.###.#.###.#..#...#..#.###.#
            #.###.#.###..#...#.#..#.###.#
            #.###.#.#...##.#.###..#.###.#
            #.....#...##.#.###.#..#.....#
            #######.#.#.#.#.#.#.#.#######
            ..........##.#.#.##..........
            ####..#.#.#.#..#####.#..###.#
            #####..##.##.##..#.##.#.####.
            ###..##.###.##.###...##.##...
            ##.##....###........#.##.#...
            ...#####.####.##.#.##..#.#.##
            .##....###..#.##..#....#.##..
            .####.####.#...##.##..#.##..#
            #.#.#...##.#.###.#.#....#....
            ##.#..##.###.....#.#.#..####.
            ..#.#..#.#.##..##..##.#..##.#
            #.....##...#....###.#..###.#.
            ..#..#..##.#.###......#.#.#..
            .##.#.###.#..###...######...#
            ........##.#.#.#..###...####.
            #######..#.....#..#.#.#.#....
            #.....#...#.#.##.##.#...##...
            #.###.#..###..###.#.#####.##.
            #.###.#.##.##.......##.##.###
            #.###.#.##.##..#..##..#...#.#
            #.....#.#.##.#.#.###.##.#..#.
            #######.##.##...##.######..#.
            """);
    }

    // ───── Version group transitions (CCI bits change) ─────

    [Fact]
    public void Create_Numeric_Version10_CCIBitsChange()
    {
        // Versions 10+ use 12-bit CCI for numeric (vs 10-bit for 1-9)
        var qr = QRCode.Create(new string('1', 272), ErrorCorrectionLevel.L);

        Assert.True(qr.Version >= 6);
        Assert.True(qr.Size >= 41);
    }

    [Fact]
    public void Create_Alphanumeric_Version10_CCIBitsChange()
    {
        // Versions 10+ use 11-bit CCI for alphanumeric (vs 9-bit for 1-9)
        var qr = QRCode.Create(new string('A', 175), ErrorCorrectionLevel.L);

        Assert.True(qr.Version >= 6);
        Assert.True(qr.Size >= 41);
    }

    [Fact]
    public void Create_Numeric_Version27_CCIBitsChange()
    {
        // Versions 27+ use 14-bit CCI for numeric (vs 12-bit for 10-26)
        var qr = QRCode.Create(new string('1', 2200), ErrorCorrectionLevel.L);

        Assert.True(qr.Version >= 21);
        Assert.True(qr.Size >= 101);
    }

    // ───── Kanji encoding edge cases ─────

    [Fact]
    public void Create_Kanji_E040Range()
    {
        // Kanji character in the 0xE040-0xEBBF Shift JIS range
        var qr = QRCode.Create("纊", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.###.#######
            #.....#...##..#.....#
            #.###.#.##.#..#.###.#
            #.###.#.##..#.#.###.#
            #.###.#.#..#..#.###.#
            #.....#..####.#.....#
            #######.#.#.#.#######
            ...........##........
            ####..#.######..###.#
            ##...#...######....##
            ..######.###.....##..
            #.#.#....#.#..###.#.#
            ..###.##..#.#..#..##.
            ........##.#..#..##..
            #######..####..#..#.#
            #.....#..##....##.###
            #.###.#..##.######..#
            #.###.#.#.##..######.
            #.###.#.##..#.##.....
            #.....#.###..#.#..#.#
            #######.#....#..#....
            """);
    }

    [Fact]
    public void Create_Byte_MixedCJKAndASCII_FallsToByteMode()
    {
        // Mixed CJK and ASCII cannot use Kanji mode - falls to byte
        var qr = QRCode.Create("Hello漢字World", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######..##...#######
            #.....#.###...#.....#
            #.###.#.#.###.#.###.#
            #.###.#..#..#.#.###.#
            #.###.#.###...#.###.#
            #.....#.####..#.....#
            #######.#.#.#.#######
            ........#..##........
            ##.#..##..###.###.##.
            #.#.##...#..##.##..##
            ...#.###..####..###.#
            ..#....##..##..#.#.##
            .#.#..####.#...#.....
            ........##.###....#..
            #######.#...##...###.
            #.....#...#.#####..#.
            #.###.#..#.###.#...##
            #.###.#.#..#...####.#
            #.###.#....#####..#.#
            #.....#.##..#.#......
            #######.###.##.#.#.#.
            """);
    }

    // ───── Properties ─────

    [Fact]
    public void Create_StandardQR_HasCorrectType()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);

        Assert.Equal(QRCodeType.Standard, qr.Type);
        Assert.Equal(qr.Width, qr.Height);
        Assert.Equal(qr.Width, qr.Size);
    }

    // ───── Error cases ─────

    [Fact]
    public void Create_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => QRCode.Create((string)null!));
    }

    [Fact]
    public void Create_EmptyData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QRCode.Create(""));
    }

    [Fact]
    public void Create_EmptyBinaryData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QRCode.Create(Array.Empty<byte>(), ErrorCorrectionLevel.M));
    }

    [Fact]
    public void Create_DataTooLong_ThrowsInvalidOperationException()
    {
        var longData = new string('A', 10000);

        Assert.Throws<InvalidOperationException>(() => QRCode.Create(longData));
    }

    // ───── Determinism ─────

    [Fact]
    public void Create_Deterministic_SameInputProducesSameOutput()
    {
        var qr1 = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.M);
        var qr2 = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.M);

        Assert.Equal(qr1.Size, qr2.Size);
        for (var row = 0; row < qr1.Size; row++)
        {
            for (var col = 0; col < qr1.Size; col++)
            {
                Assert.Equal(qr1[row, col], qr2[row, col]);
            }
        }
    }

    [Fact]
    public void Create_DifferentData_ProducesDifferentOutput()
    {
        var qr1 = QRCode.Create("AAA", ErrorCorrectionLevel.M);
        var qr2 = QRCode.Create("BBB", ErrorCorrectionLevel.M);

        var different = false;
        for (var row = 0; row < qr1.Size && !different; row++)
        {
            for (var col = 0; col < qr1.Size && !different; col++)
            {
                if (qr1[row, col] != qr2[row, col])
                {
                    different = true;
                }
            }
        }

        Assert.True(different);
    }

    private static string RenderAsText(QRCode qr)
    {
        var sb = new StringBuilder();
        for (var row = 0; row < qr.Size; row++)
        {
            if (row > 0)
            {
                sb.AppendLine();
            }

            for (var col = 0; col < qr.Size; col++)
            {
                sb.Append(qr[row, col] ? '#' : '.');
            }
        }

        return sb.ToString();
    }

    private static TheoryData<int, int> CreateNumericPayloadLengthForAllVersions()
    {
        var data = new TheoryData<int, int>();
        var versionByLength = new Dictionary<int, int>();

        var minLength = 1;
        const int MaxLength = 7089;
        for (var version = 1; version <= 40; version++)
        {
            var payloadLength = FindMinimumLengthForVersion(version, minLength, MaxLength, versionByLength);
            data.Add(version, payloadLength);
            minLength = payloadLength + 1;
        }

        return data;
    }

    private static int FindMinimumLengthForVersion(int expectedVersion, int minLength, int maxLength, Dictionary<int, int> versionByLength)
    {
        var left = minLength;
        var right = maxLength;
        var bestLength = maxLength;

        while (left <= right)
        {
            var middle = left + ((right - left) / 2);
            var actualVersion = GetVersionForNumericPayloadLength(middle, versionByLength);
            if (actualVersion >= expectedVersion)
            {
                bestLength = middle;
                right = middle - 1;
            }
            else
            {
                left = middle + 1;
            }
        }

        var resolvedVersion = GetVersionForNumericPayloadLength(bestLength, versionByLength);
        if (resolvedVersion != expectedVersion)
        {
            throw new InvalidOperationException($"Unable to find payload for QR version {expectedVersion}. Resolved version: {resolvedVersion}.");
        }

        return bestLength;
    }

    private static int GetVersionForNumericPayloadLength(int payloadLength, Dictionary<int, int> versionByLength)
    {
        if (!versionByLength.TryGetValue(payloadLength, out var version))
        {
            version = QRCode.Create(new string('1', payloadLength), ErrorCorrectionLevel.L).Version;
            versionByLength[payloadLength] = version;
        }

        return version;
    }
}
