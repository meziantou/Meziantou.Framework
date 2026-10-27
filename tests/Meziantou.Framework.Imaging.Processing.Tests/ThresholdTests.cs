using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Binarization: Otsu's threshold, fixed thresholds and the adaptive Mean and Sauvola methods. The expectations are
/// hand-computed, or come from scikit-image 0.25.2 (numpy 2.5.4, scipy 1.18.1, in a <c>python:3.13-slim</c> container):
/// <c>threshold_otsu</c> on <see cref="Pattern"/>, <see cref="Textured"/> and <see cref="OtsuFamily8"/>, and the masks
/// <c>image &gt; threshold_local(image, w, method='mean', offset, mode)</c> and <c>image &gt; threshold_sauvola(image, w, k, r)</c>
/// of <see cref="Textured"/>, packed by <c>np.packbits</c> (most significant bit first) and kept as literals. The scipy
/// modes <c>reflect</c>, <c>nearest</c>, <c>wrap</c> and <c>constant</c> are the edge modes <c>Mirror</c>, <c>Clamp</c>,
/// <c>Wrap</c> and <c>Zero</c>; <c>threshold_sauvola</c> has no mode, so it ran on the image padded by <c>np.pad</c>
/// (<c>symmetric</c>, <c>edge</c>, <c>wrap</c>, <c>constant</c>) by half the window, and the result was cropped.
/// </summary>
public sealed class ThresholdTests
{
    private const int Width = 64;
    private const int Height = 48;

    private static readonly Dictionary<string, string> ScikitImageMasks = new(StringComparer.Ordinal)
    {
        ["mean8_Mirror"] = "F9BOvs0KFm8TJkyDhZHMbyjfQX0F9ihmZ1tCgmTJmFFYo2565hfQX65jOYzr7FDhG0OCZMmQcL5hQs30lGcxSZzGczQTrx9+NsUKFCjbXoJCzPQW11NBe4L6GZeds2bNMnXrkzCjPQVyCyOYziaCdXM0E68ffvj19evTIKN9BfTkF9JfQTr7NuvsUOCBAwo3CDhRvrmM5jsX0F9BOvs2KChmTr1yZBRn16ZhbXMZ1tCorraGZevXr1DMmTIKFtcxrkFGcxnMUE5fQzr7NChRt7NmzfHpmN9BoX0ltJfQoJ1BOvs0KFCzfUKFCzPQUV1tgvpL6CdfZoWGZevHj16ZhYUb65BbSX0EjOYpLzNChw77FChQs31yC/TML66iutodC+gnX2aFCjcUKFChbHoLa+mYX0F9DMvPF9igmXr16ZBs2b69BRnoL5jfQX0My8fZ0My9ePHj0yBw4Ub6ZjOY7KC+gvoUEy9eoJ169euQUL5hQtrkF9DOvkF5ikvH2bNm",
        ["mean8_Clamp"] = "F9BOvsUKFG0zJkyDhbXMLyzfQW0F9ihmZ1tCgmTJmFHZo2Z65hfQX65jOYzr7FDhW0OCZMmQcL7hQs30lGcxSZzGczQTrx9+PsUKFCjbXoJCzPQW11NBe4L6GZeds2bNMnXrkzCjPQVyCyOYziaCdXM0E68ffvj19evTIKN9BfTkF9JfQTr7NOvsUOCBAwo3CDhRvrmM5jMX0F9BOvs2KChmTr1yZBRn16ZhbXMZ1tCorraGZevXr1DMmTIKFtcxrkFGcxnMUE5fQzr7NChRs7NmzfHpmN9BIX0ltJfQoJ1BOvs0KFCzfUKFCzPQUV1NgvpL6CdfZoWGZevHj16ZhYUb65BbSX0EjOYpLzNChw77FChQs31yC/TML66iutodC+gnX2aFCjYUKFChbHoLa+mYX0F9DMvPF9igmXr16Zhs2b69BRnoL5jfQX0My8fZ0My9ePHj0zBw4Ub6ZjOY7KC2gvsUEy9eoJ169euQUL5hQvrmF9BOvkF5ikvH2bNm",
        ["mean8_Wrap"] = "B9BOvs0KFH8TJkyDhZHIfwDfQX0F9ih3B1tCgmTJkF8Ao2Z65hfQXw5jOYzr6FDnAUOCZMmQcL8BQs30lGcxTxzGczQTrx9/AsUKFCjbXs8CzPQW11NBfwL6GZeds2b/MnXrkzCjPQ8SCyOYziaCfxM0E68ffvj/EevTIKN9BfcEF9JfQTr7fyPsUOCBAwo/CDhRvrmM5j8H0F9BOvs2bwBmTr1yZBR3B6ZhbXMZ1t8ArraGZevXrwDMmTIKFtd/DkFGcxnMUG8TQzr7NChRvwFmzfHpmN9vAX0ltJfQoJ8BOvs0KFCzfwCFCzPQUV1/AvpL6CdfZt8CZevHj16ZjwUb65BbSX0PBOYpLzNChx8LFChQs31yHxTML66iutofA+gnX2aFCj8EKFChbHoLbwGYX0F9DMvfB9igmXr16b8I2b69BRnofxDfQX0My8f/AMy9ePHr038g4Ub6ZjOY7wC2gvsUEy9fAJ169euQUb8BQvrmF9DOvwF5ikvX2bN/",
        ["mean8_Zero"] = "/////////////////////////////////1/+////////v/9/7/////7zu97r/9n/32OHZMm4ef/xQs30lGcxf9zGczQTrx9/vsUKFCjbXv/+zPQW11NBf+b6GZeds2b/8nXrkzCjPR/2CyOYziaCf/c0E68ffvj/9evTIKN9Bf/uF9JfQTr7f/vsUOCBAwp/vDhRvrmM5n/30F9BOvs2f+hmTr1yZBR/16ZhbXMZ1v/srraGZevXv9DMmTIKFtd/7kFGcxnMUH//Qzr7NChRv/9mzfHpmN9/u30ltJfQoN/zOvs0KFCzf+KFCzPQUV1/5vpL6CdfZv/mZevHj16Zv/0b65BbSX0f/uYpLzNChx/7FChQs31yP/TML66iuto/m+gnX2aFCn/0KFChbHoLf+mYX0F9DMvf99igmXr16b/+2b69BRnof/zfY32Oy9f/+Oy9evXr23/583/7f/u///m/z////////9//////////////////////////////",
        ["mean8_Mirror_offset8"] = "F9DOvu2bNm8zJkzDhfXMbyzfQX0F9mh2Z19ih2TJuFnZs3967jfQX67jOY7r7NDhX0OGZMmYcb7hQt30lnd7SdzGd7Qzr1//vs0KFCzfXsJG/vQX11tBe4L7HZe/s2b9MnXrk3CzPY1zC+u47raGdXe0M69ffvr19evTMLN9BfTmF9JfQzr7tuvs0OHDhws3HDhZvruM5jsX0F9DOvu2bGh2Tr1ybhb316bhfXcd19Ctrr6HZevXr1DsmTMKN9cxrmFncxnc0M5fYzr79mzZv79mzfXpuN9jo30l9JfYoZ1Duvv2bNm/fUKFG/vQW119wvpL6Gdffs2HZevXr16bjY2b65hfSX0O3u9pLztihw77NihRs31zC/TcL662uvsdC+hnX3bNGzcUKFCzfXoL6+m430F9jsvfF9mhmXr16bjv3769BRnsb7jfYX2Oy9ff0Oy9evHr03Fw4Wb6bjOY7KC+gvsUEy9eoZ169euQUb7hRvrmN9jOvmN5ikvX37t/",
        ["mean8_Mirror_101"] = "AEBOPs2b9/8DBkyDhfXf/wBRQW0F/+z/AQFAAmTdvN8AAUJ67jf4/wpBOQzr79nzAAECZMm4f/8AAIX0lvd/bwCCcxQTr1//AAACFCzfX/8ABLQU119j/wCKCZefv3//MHHrkzC/v/8AAAOYzr7PfwIAE68f//r/ASPTIKN9jf8ABEJbQ7r//yAoAECDh5v/ABAQvrve7/8AABNBOv///wAmTr1ybjf3AIZBRXe91/8ADiKGZevXvwBMmTIKN9d/AAACcxnf2e8BATj7Nm/f/wAChZHpvf//AEQhFJfdsd8BOJoULN///wAACjPQX19/AABCaCdf//8CYePHr17b/wAKI5BfSX//BKQBLz9mz/8AAAhQs313/wDIKK6muv//ACAnH2bN+/8AAAChfX8b/wGQW0F9nt/fAFAAmXr1/b8AULI9Bb3//xBRQX2Oy9f/AEw8ePXr+38AQEL6b3u//wACAvsUO3//ABx49eu4X/8AAojmN9nvvwBBAEvX////",
        ["mean16_Mirror"] = "F9BOvs0KFm8TJkyDhZHMbyjfQX0F9ihmZ1tChmTJmFFYo2565hfQX65jOYzr7FDhG0OCZMmQcL5hQs30lGcxSZzGczQTrx9+NsUKFChbXoJCzPQW11NBe4L6GZeds2bNMnXrkzCjPQVyCyOYziaCdXMUE68ffvj19evTIKN9BfTkF9JfQTr7NuvsUOCBAwo3CDhRvrmM5jsX0F9BOvs2KChmTr1yZBZn16ZhbXMZ1tCorraGZevXr1BMmTIKFtcxrkFGcxnMUE5fQzr7NChRt7NmzfHpmN9BoX0ltJfQoJ1BOvs0KFCzfUKFCzPQUV1tgvpL6CdfZoWGZevHj16ZhYUb65BbSX0EjOYpLzNChw77FChQo31yC/TML66iutodC+gnX2aFCjcUKFChbHoLa+mYX0F9DMvPF9igmXr16ZBs2b69BRnoL5jfQX0My8fZ0My9ePHj0zBw4Ub6ZjOY7KC+gvoUEy9eoJ169euQUL5hQtrkF9DOvkF5ikvH2bNm",
        ["sauvola8_Mirror"] = "F9juv/37//87du3D3fXf/y//Y32P/+7/d192x27d/N/fv39773/9/673u/7r/9/zX2eHbt24//7hZv/2l/d/a93vd/Y7r1//vv2bN+//X/9m/v43119j/8b/vbe/////t3Xvu3m/v993G+u/7r7Pf3f2O69f//r/9evbcb//j//uN9rfZ7r//+v/2eHDj7v/HDjf/rv+7/832N9juv///2x277/3bzf316bjfXf99/+vrr7Pb+vXr9Dt27cb/9d/ruN/d73f2e/fY7r//+////9+//X9v///t/2l9rf/s99juv//79///0LN+/v4339/xvpb7Hdf///HbevXr1/f/83/67jfa3///u/pb39+z7/79mzfv/13v/Teb66+vv//C+x3X//9//82bNm//X8b7+m8/2N//t/fN/+x2/v//b/v//6/jf3//73/4//+39f/2Oy9evXr+39w83/6b/u//bG+xv/2O3//od169e+43//hbvrvf//uv+P7+l/X////",
        ["sauvola8_Clamp"] = "N9juv//7//87du3D3fXf/2//Y32P/+//9192h27d/N/fv3977//9/673u/7r/9/zX2eHbt24//7hZv/2l/d/a93vd/Y7r1//vv2bN+//X/9m/v43119j+8b/vbe/////t3Xvu3m/v993G+u/7r7Pf3f2O69f//r/9evbcb//j//uN9rfZ7r//+v/2eHDj7v/HDjf/rv+7/832N9juv///2x277/3bzf316bjfXf99/+vrr7Pb+vXr9Dt27cb/9d/ruN/d73f2e9fY7r//+///79+//X9v///t/2l9rf/s99juv//79///0LN+/v4339/xvpb7Hdf///HbevXr1/f/83/67jfa3/e/u/pb39+z5/79mzfv/13v/Teb66+vv//C+x3X//9//82bNm//X8b7+m8/2N//t/fN/+x2/v//b/v//6/jf3//73/4//+39f/2Oy9evXr+39w83/6b/u//bG+xv/2O3//od169eu43//hZvrvf/3uv+P7+l/X////",
        ["sauvola8_Wrap"] = "F9juv/379/8Tdu3D3fXf/wn/Y32P/+z/J192x27dvN8Rv39773/9/w73u/7r/9//E2eHbt24//9BZv/2l/d/f53vd/Y7r1//Bv2bN+//X/8C/v43119j/wL/vbe/////M3Xvu3m/v/9zG+u/7r7Pf3P2O69f//r/tevbcb//j/8mN9rfZ7r//+v/2eHDj7v/CDjf/rv+7/8X2N9juv///wh277/3bzf/B6bjfXf99/+Jrr7Pb+vXvwDt27cb/9d/DuN/d73f2f8fY7r//+///6N+//X9v///A/2l9rf/s/8Duv//79///wLN+/v4339/Avpb7Hdf//8HbevXr1/f/wX/67jfa3//Du/pb39+z/8L9mzfv/13/zTeb66+vv//C+x3X//9//8UbNm//X8b/ym8/2N//t/fF/+x2/v//f8v//6/jf3//5n/4//+39f/EOy9evXr+38g83/6b/u//wG+xv/2O3//Ad169e+43/9BZvrvf//vvwP7+l/X////",
        ["sauvola8_Zero"] = "/////////////////////////////////9////////////9///////7/u////////2eHbt28///zZv/2l/d/f/3vd/Y7r1///v2bN+//X//+/v43119j/+b/vbe/////93Xvu3m/v//3G+u/7r7Pf/f2O69f//r//evbcb//j//uN9rfZ7r///v/2eHDj7v//Djf/rv+7///2N9juv///+x277/3bzf/96bjfXf99//vrr7Pb+vXv9jt27cb/9d//uN/d73f2f//Y7r//+////9+//X9v/////2l9rf/s///uv//79////bN+/v4339//vpb7Hdf////bevXr1/f//3/67jfa3///u/pb39+z///9mzfv/13//7eb66+vv//++x3X//9///2bNm//X8b//28/2N//t////+x2/v//b////6/jf3/////4//+39f//e39evXv+///////f///////////////////////////////////////////////",
        ["sauvola8_Mirror_k05_r025"] = "N//v//////87du/n/////2//9///////91/+//////////9//////77/u///////X//P///////zf///v////93/f///////v//7//////9//v9//////8f//7//////t3//v/////93v+///////3f3e//////////7///////vf/r///////v/3/Pn////Hn/f//////8/+P///////+z+7//3////1/b///////+vv7///////9n92///////vvN/9////////7////////////f/////v/+v////////v////////2b9////////7/9f///////vb///////////77///////u/t/3////////////////7/////////G//3///////379////////2/////////P/+7+///////////3////7//////////3e3/evf////5////f/////H/z///////sd/////////zf/vv///////7+3//////",
        ["sauvola8_Mirror_31"] = "F9juv/379/87du3D3fXf/y3/Y32P/+7/Z192h27dvN/Zv3977//9/673u/7r/9//X2eHbt24///hZv/2l/d/b93vd/Y771//vv2bNu//X/9m/vY3119j/4b/vbe/////snXvu3m/v/93G+u/7r7Pf3f2O69f//r/9evbcb//j//mN9rfZ7r//+v/2eHDj7v/HDjf/rv+7/832N9nuv///2h27r/3bzf316bjfXf99/+vrr7Pb+vXv1Dt27cb/9d/ruF/d73f2e9fY7r//+///79+//Xtv///o32l9Lf/s99juv/379///0KN+/v4319/xvpL7Hdf//+HZevXr1/b/43/67jfa3//3u/pb39+z//79mzfv/13//Tcb66+vv//C+x3X/79+/82bNm//X8b/+m8/2N//t/fF/+x2/r//f/v//6/jf3//7z/Y//+39f/2Oy9evXr+39w83/6b/u//aC+xv/2O3//od169e+43/9hZvrvf//vv2P7+l/X////",
        ["sauvola16_Mirror"] = "F9juv/37//87du3D3fXf/y//Y32P/+//d192x27dvN/fv39773/9/673u/7r/9/zX2eHbt24//7hZv/2l/d/b93vd/Y7r1//vv2bN+//X/9m/v43119j+8b/vbe/////t3Xvu3m/v993G+u/7r7Pf3f2O69f//r/9evbcb//j//uN9rfZ7r//+v/2eHDj7v/HDjf/rv+7/832N9nuv///2x27r/3bzf316bjfXf99/+vrr7Pb+vXr9Dt27cb/99/ruN/d/3f2e/fY7r//+////9+//X9v///t/2l9rf/s99juv/379///0LN+/v4339/xvtb7Pdf///HbevXr1/f/83/67jfa3/f/u/pb39+z5//9mzfv/13v/Teb66+vv//C+x3X//9//82bNm//X8b7+m8/2N//t/fN/+x2/r//b/v//6/jf3//73/4//+39f/2Oy9evXr+39w83/6b/u//bG+xv/2O3//od169e+43//hbvrvf//uv+P7+l/X////",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -----------------------------------------------------------------------------------------------------------------
    // Otsu's method
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void OtsuThresholdMatchesScikitImage()
    {
        using var pattern = CreateGray8(Pattern);
        Assert.Equal(164, pattern.ComputeHistogram(HistogramChannel.Luminance).GetOtsuThreshold());

        using var textured = CreateGray8(Textured);
        Assert.Equal(169, textured.ComputeHistogram(HistogramChannel.Luminance).GetOtsuThreshold());

        using var textured16 = CreateGray16(Textured);
        var histogram16 = textured16.ComputeHistogram(HistogramChannel.Luminance);
        Assert.Equal(65536, histogram16.BinCount);
        Assert.Equal(43596, histogram16.GetOtsuThreshold());

        // Two levels in each mode: every threshold from 11 to 199 separates the same classes, and the first one wins
        using var bimodal = Gray8Row(10, 10, 10, 10, 10, 11, 11, 11, 200, 200, 200, 200, 201, 201);
        Assert.Equal(11, bimodal.ComputeHistogram(HistogramChannel.Luminance).GetOtsuThreshold());
    }

    [Theory]
    [InlineData(0, 123, 31791)]
    [InlineData(1, 124, 32752)]
    [InlineData(2, 126, 32511)]
    [InlineData(3, 122, 32578)]
    [InlineData(4, 129, 32299)]
    [InlineData(5, 124, 32752)]
    [InlineData(6, 125, 32755)]
    [InlineData(7, 126, 32702)]
    [InlineData(8, 125, 32445)]
    [InlineData(9, 125, 32694)]
    [InlineData(10, 125, 32693)]
    [InlineData(11, 128, 32712)]
    public void OtsuThresholdMatchesScikitImageOnAFamilyOfImages(int seed, int expected8, int expected16)
    {
        using var image8 = new Image<Gray8>(32, 24);
        using var image16 = new Image<Gray16>(32, 24);
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                image8.Frames[0][x, y] = new Gray8(OtsuFamily8(seed, x, y));
                image16.Frames[0][x, y] = new Gray16((ushort)(((x * x * (seed + 1) * 251) + (y * (3 + seed) * 997) + ((x * y * 13) % (7 + seed))) % 65536));
            }
        }

        Assert.Equal(expected8, image8.ComputeHistogram(HistogramChannel.Luminance).GetOtsuThreshold());
        Assert.Equal(expected16, image16.ComputeHistogram(HistogramChannel.Luminance).GetOtsuThreshold());
    }

    [Fact]
    public void OtsuThresholdOfASingleLevelIsThatLevel()
    {
        using var image = Gray8Row(42, 42, 42);
        Assert.Equal(42, image.ComputeHistogram(HistogramChannel.Luminance).GetOtsuThreshold());

        // Every pixel is at the threshold, so none is above it
        image.Threshold(cancellationToken: Ct);
        Assert.Equal([0, 0, 0], ReadGray8(image.Frames[0]));
    }

    [Fact]
    public void OtsuPutsThePixelsAboveTheThresholdInTheUpperClass()
    {
        using var image = CreateGray8(Textured);
        image.Threshold(cancellationToken: Ct);

        var pixels = ReadGray8(image.Frames[0]);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                Assert.Equal(Textured(x, y) > 169 ? 255 : 0, pixels[(y * Width) + x]);
            }
        }
    }

    [Fact]
    public void EachFrameGetsItsOwnOtsuThresholdUnlessTheHistogramIsShared()
    {
        // Frames [10, 20], [30, 40] and a poster [50, 60]
        using (var image = CreateTwoFrames())
        {
            image.Threshold(cancellationToken: Ct);
            Assert.Equal([0, 255], ReadGray8(image.Frames[0]));
            Assert.Equal([0, 255], ReadGray8(image.Frames[1]));
            Assert.Equal([0, 255], ReadGray8(image.PosterFrame!));
        }

        // The threshold of the six levels together is 30: the first frame is dark, the poster is light
        using (var image = CreateTwoFrames())
        {
            image.Threshold(new ThresholdOptions { ShareHistogramAcrossFrames = true }, Ct);
            Assert.Equal([0, 0], ReadGray8(image.Frames[0]));
            Assert.Equal([0, 255], ReadGray8(image.Frames[1]));
            Assert.Equal([255, 255], ReadGray8(image.PosterFrame!));
        }

        // One frame alone ignores the setting
        using (var image = CreateTwoFrames())
        {
            image.Frames[0].Threshold(new ThresholdOptions { ShareHistogramAcrossFrames = true }, Ct);
            Assert.Equal([0, 255], ReadGray8(image.Frames[0]));
            Assert.Equal([30, 40], ReadGray8(image.Frames[1]));
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Fixed threshold, colors and alpha
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void FixedThresholdIsAFractionOfTheSampleRange()
    {
        using var image = Gray8Row(0, 127, 128, 255);
        image.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed }, Ct);
        Assert.Equal([0, 0, 255, 255], ReadGray8(image.Frames[0]));

        using var image16 = new Image<Gray16>(3, 1);
        image16.Frames[0][0, 0] = new Gray16(32767);
        image16.Frames[0][1, 0] = new Gray16(32768);
        image16.Frames[0][2, 0] = new Gray16(49151);
        image16.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed, Level = 0.75 }, Ct);
        Assert.Equal(0, image16.Frames[0][0, 0].Value);
        Assert.Equal(0, image16.Frames[0][1, 0].Value);
        Assert.Equal(0, image16.Frames[0][2, 0].Value);

        // 0.75 * 65535 = 49151.25: 49152 is the first level above it
        image16.Frames[0][2, 0] = new Gray16(49152);
        image16.Frames[0].Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed, Level = 0.75 }, Ct);
        Assert.Equal(65535, image16.Frames[0][2, 0].Value);

        // Level 0 puts every pixel but black in the upper class, level 1 none
        using var extremes = Gray8Row(0, 1, 255);
        extremes.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed, Level = 0 }, Ct);
        Assert.Equal([0, 255, 255], ReadGray8(extremes.Frames[0]));
        extremes.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed, Level = 1 }, Ct);
        Assert.Equal([0, 0, 0], ReadGray8(extremes.Frames[0]));
    }

    [Fact]
    public void ColorPixelsAreComparedByTheLuminanceThatGrayscaleStores()
    {
        using var color = new Image<Rgb24>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var level = Textured(x, y);
                color.Frames[0][x, y] = new Rgb24((byte)level, (byte)(255 - level), (byte)((x * 4) % 256));
            }
        }

        using var gray = color.CloneAs<Rgb24>();
        gray.Grayscale(Ct);

        var options = new ThresholdOptions { Method = ThresholdMethod.Sauvola };
        color.Threshold(options, Ct);
        gray.Threshold(options, Ct);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var pixel = color.Frames[0][x, y];
                Assert.True(pixel == new Rgb24(0, 0, 0) || pixel == new Rgb24(255, 255, 255));
                Assert.Equal(gray.Frames[0][x, y], pixel);
            }
        }
    }

    [Fact]
    public void EveryPixelGetsOneOfTheTwoColorsAlphaIncluded()
    {
        using var image = new Image<Rgba32>(4, 1);
        image.Frames[0][0, 0] = new Rgba32(10, 10, 10, 255);
        image.Frames[0][1, 0] = new Rgba32(250, 250, 250, 0);
        image.Frames[0][2, 0] = new Rgba32(10, 10, 10, 128);
        image.Frames[0][3, 0] = new Rgba32(250, 250, 250, 128);

        var upper = new Rgba64(65535, 0, 0, 32896);
        var lower = new Rgba64(0, 0, 0, 0);
        image.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed, UpperColor = upper, LowerColor = lower }, Ct);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 128), image.Frames[0][1, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image.Frames[0][2, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 128), image.Frames[0][3, 0]);

        using var bgra = new Image<Bgra32>(1, 1, new Bgra32(255, 255, 255, 255));
        bgra.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed, UpperColor = new Rgba64(0, 257, 514, 771) }, Ct);
        Assert.Equal(new Bgra32(0, 1, 2, 3), bgra.Frames[0][0, 0]);
    }

    [Fact]
    public void ColorsThePixelFormatCannotStoreAreRejected()
    {
        using var rgb = new Image<Rgb24>(1, 1, new Rgb24(200, 200, 200));
        Assert.Throws<ArgumentException>("options", () => rgb.Threshold(new ThresholdOptions { UpperColor = new Rgba64(65535, 65535, 65535, 0) }, Ct));
        Assert.Throws<ArgumentException>("options", () => rgb.Threshold(new ThresholdOptions { LowerColor = new Rgba64(1000, 0, 0) }, Ct));

        using var gray = Gray8Row(200);
        Assert.Throws<ArgumentException>("options", () => gray.Frames[0].Threshold(new ThresholdOptions { LowerColor = new Rgba64(0, 0, 65535) }, Ct));
        Assert.Equal(new Rgb24(200, 200, 200), rgb.Frames[0][0, 0]);
        Assert.Equal([200], ReadGray8(gray.Frames[0]));

        // A 16-bit format stores any 16-bit sample
        using var rgba64 = new Image<Rgba64>(1, 1, new Rgba64(1000, 1000, 1000));
        rgba64.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed, LowerColor = new Rgba64(1, 2, 3, 4) }, Ct);
        Assert.Equal(new Rgba64(1, 2, 3, 4), rgba64.Frames[0][0, 0]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Adaptive methods
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("mean8_Mirror", ThresholdMethod.Mean, ConvolutionEdgeMode.Mirror, 15, 0.0, 0.2, 0.5)]
    [InlineData("mean8_Clamp", ThresholdMethod.Mean, ConvolutionEdgeMode.Clamp, 15, 0.0, 0.2, 0.5)]
    [InlineData("mean8_Wrap", ThresholdMethod.Mean, ConvolutionEdgeMode.Wrap, 15, 0.0, 0.2, 0.5)]
    [InlineData("mean8_Zero", ThresholdMethod.Mean, ConvolutionEdgeMode.Zero, 15, 0.0, 0.2, 0.5)]
    [InlineData("mean8_Mirror_offset8", ThresholdMethod.Mean, ConvolutionEdgeMode.Mirror, 15, 8.0 / 255, 0.2, 0.5)]
    [InlineData("mean8_Mirror_101", ThresholdMethod.Mean, ConvolutionEdgeMode.Mirror, 101, 0.0, 0.2, 0.5)]
    [InlineData("sauvola8_Mirror", ThresholdMethod.Sauvola, ConvolutionEdgeMode.Mirror, 15, 0.0, 0.2, 0.5)]
    [InlineData("sauvola8_Clamp", ThresholdMethod.Sauvola, ConvolutionEdgeMode.Clamp, 15, 0.0, 0.2, 0.5)]
    [InlineData("sauvola8_Wrap", ThresholdMethod.Sauvola, ConvolutionEdgeMode.Wrap, 15, 0.0, 0.2, 0.5)]
    [InlineData("sauvola8_Zero", ThresholdMethod.Sauvola, ConvolutionEdgeMode.Zero, 15, 0.0, 0.2, 0.5)]
    [InlineData("sauvola8_Mirror_k05_r025", ThresholdMethod.Sauvola, ConvolutionEdgeMode.Mirror, 15, 0.0, 0.5, 0.25)]
    [InlineData("sauvola8_Mirror_31", ThresholdMethod.Sauvola, ConvolutionEdgeMode.Mirror, 31, 0.0, 0.2, 0.5)]
    public void AdaptiveThresholdsMatchScikitImage(string name, ThresholdMethod method, ConvolutionEdgeMode edgeMode, int windowSize, double offset, double k, double r)
    {
        using var image = CreateGray8(Textured);
        image.Threshold(new ThresholdOptions { Method = method, EdgeMode = edgeMode, WindowSize = windowSize, Offset = offset, SauvolaK = k, SauvolaR = r }, Ct);
        AssertMask(ScikitImageMasks[name], [.. ReadGray8(image.Frames[0]).Select(value => value == 255)]);
    }

    [Theory]
    [InlineData("mean16_Mirror", ThresholdMethod.Mean)]
    [InlineData("sauvola16_Mirror", ThresholdMethod.Sauvola)]
    public void AdaptiveThresholdsOf16BitSamplesMatchScikitImage(string name, ThresholdMethod method)
    {
        using var image = CreateGray16(Textured);
        image.Threshold(new ThresholdOptions { Method = method }, Ct);

        var pixels = new Gray16[Width * Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        Assert.All(pixels, pixel => pixel.Value is 0 or 65535);
        AssertMask(ScikitImageMasks[name], [.. pixels.Select(pixel => pixel.Value == 65535)]);
    }

    [Fact]
    public void MeanThresholdIsTheMeanOfTheWindowMinusTheOffset()
    {
        // Windows of 3 pixels, mirrored at the edges (the window of the first pixel is 10, 10, 20): means 13.3, 20, 30, 36.7
        using var image = Gray8Row(10, 20, 30, 40);
        image.Threshold(new ThresholdOptions { Method = ThresholdMethod.Mean, WindowSize = 3 }, Ct);
        Assert.Equal([0, 0, 0, 255], ReadGray8(image.Frames[0]));

        // A uniform area is at its mean, which is not above it; an offset moves it to the upper class
        using var uniform = Gray8Row(50, 50, 50, 50);
        uniform.Threshold(new ThresholdOptions { Method = ThresholdMethod.Mean, WindowSize = 3 }, Ct);
        Assert.Equal([0, 0, 0, 0], ReadGray8(uniform.Frames[0]));
        uniform.Frames[0].ProcessPixelRows(pixels => pixels.GetRowSpan(0).Fill(new Gray8(50)));
        uniform.Threshold(new ThresholdOptions { Method = ThresholdMethod.Mean, WindowSize = 3, Offset = 1.0 / 255 }, Ct);
        Assert.Equal([255, 255, 255, 255], ReadGray8(uniform.Frames[0]));
    }

    [Fact]
    public void SauvolaKeepsUniformAreasInTheUpperClass()
    {
        // Without contrast, the threshold is (1 - k) times the mean
        using var image = Gray8Row(100, 100, 100);
        image.Threshold(new ThresholdOptions { Method = ThresholdMethod.Sauvola, WindowSize = 3 }, Ct);
        Assert.Equal([255, 255, 255], ReadGray8(image.Frames[0]));

        // Black stays black: its threshold is 0
        using var black = Gray8Row(0, 0, 0);
        black.Threshold(new ThresholdOptions { Method = ThresholdMethod.Sauvola, WindowSize = 3 }, Ct);
        Assert.Equal([0, 0, 0], ReadGray8(black.Frames[0]));
    }

    [Theory]
    [InlineData(ThresholdMethod.Mean, ConvolutionEdgeMode.Mirror)]
    [InlineData(ThresholdMethod.Mean, ConvolutionEdgeMode.Wrap)]
    [InlineData(ThresholdMethod.Sauvola, ConvolutionEdgeMode.Clamp)]
    [InlineData(ThresholdMethod.Sauvola, ConvolutionEdgeMode.Zero)]
    public void AdaptiveThresholdOfAMirroredFrameIsTheMirroredResult(ThresholdMethod method, ConvolutionEdgeMode edgeMode)
    {
        var options = new ThresholdOptions { Method = method, EdgeMode = edgeMode, WindowSize = 9 };
        using var image = CreateGray8(Textured);
        using var mirrored = image.CloneAs<Gray8>();
        mirrored.Flip(FlipMode.Horizontal, Ct);
        mirrored.Flip(FlipMode.Vertical, Ct);
        image.Threshold(options, Ct);
        mirrored.Threshold(options, Ct);
        mirrored.Flip(FlipMode.Horizontal, Ct);
        mirrored.Flip(FlipMode.Vertical, Ct);
        Assert.Equal(ReadGray8(image.Frames[0]), ReadGray8(mirrored.Frames[0]));
    }

    [Fact]
    public void AdaptiveWindowsCanBeLargerThanTheFrame()
    {
        // With a window much larger than the frame, every edge mode still reads a full window: about 20 for Clamp (mostly
        // copies of the edges), 80 for Mirror and Wrap (the three levels in turn), and almost 0 for Zero
        foreach (var edgeMode in Enum.GetValues<ConvolutionEdgeMode>())
        {
            using var image = Gray8Row(10, 200, 30);
            image.Threshold(new ThresholdOptions { Method = ThresholdMethod.Mean, WindowSize = 20001, EdgeMode = edgeMode }, Ct);
            byte[] expected = edgeMode switch
            {
                ConvolutionEdgeMode.Clamp => [0, 255, 255],
                ConvolutionEdgeMode.Zero => [255, 255, 255],
                _ => [0, 255, 0],
            };
            Assert.Equal(expected, ReadGray8(image.Frames[0]));
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Bilevel output
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ThresholdMethod.Otsu)]
    [InlineData(ThresholdMethod.Sauvola)]
    public void BinarizedImagesRoundTripThroughBilevelTiffAndPortableBitmaps(ThresholdMethod method)
    {
        using var image = new Image<Rgba32>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var level = (byte)Textured(x, y);
                image.Frames[0][x, y] = new Rgba32(level, level, (byte)(level / 2), 255);
            }
        }

        image.Threshold(new ThresholdOptions { Method = method }, Ct);
        var expected = new Rgba32[Width * Height];
        image.Frames[0].CopyPixelDataTo(expected);
        var expectedGray = expected.Select(pixel => pixel.R).ToArray();
        Assert.Contains((byte)0, expectedGray);
        Assert.Contains((byte)255, expectedGray);

        foreach (var compression in new[] { TiffCompression.CcittGroup4, TiffCompression.CcittGroup3, TiffCompression.Deflate })
        {
            using var tiff = new MemoryStream();
            image.Save(tiff, new TiffEncoder { PixelLayout = TiffPixelLayout.Bilevel, Compression = compression });
            tiff.Position = 0;
            using var decoded = Image.Load<Gray8>(tiff);
            Assert.Equal(expectedGray, ReadGray8(decoded.Frames[0]));
        }

        foreach (var encoding in new[] { PnmEncoding.Binary, PnmEncoding.Plain })
        {
            using var pbm = new MemoryStream();
            image.Save(pbm, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, Encoding = encoding });
            Assert.Equal(encoding == PnmEncoding.Binary ? (byte)'4' : (byte)'1', pbm.ToArray()[1]);
            pbm.Position = 0;
            using var decoded = Image.Load<Gray8>(pbm);
            Assert.Equal(expectedGray, ReadGray8(decoded.Frames[0]));
        }
    }

    [Fact]
    public void DitheringToBlackAndWhiteIsAQuantizationToTwoColors()
    {
        using var image = CreateGray8(Textured);
        image.Quantize(new QuantizeOptions
        {
            Palette = new ImagePalette([new Rgba32(0, 0, 0), new Rgba32(255, 255, 255)]),
            Dithering = DitheringMode.FloydSteinberg,
        });

        Assert.All(ReadGray8(image.Frames[0]), value => value is 0 or 255);
        using var tiff = new MemoryStream();
        image.Save(tiff, new TiffEncoder { PixelLayout = TiffPixelLayout.Bilevel, Compression = TiffCompression.CcittGroup4 });
        tiff.Position = 0;
        using var decoded = Image.Load<Gray8>(tiff);
        Assert.Equal(ReadGray8(image.Frames[0]), ReadGray8(decoded.Frames[0]));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void InvalidOperationsChangeNothing()
    {
        using var image = Gray8Row(10, 20, 30);
        Assert.Throws<ArgumentNullException>(() => ((Image)null!).Threshold());
        Assert.Throws<ArgumentNullException>(() => ((ImageFrame)null!).Threshold());

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => image.Threshold(cancellationToken: canceled.Token));
        Assert.Throws<OperationCanceledException>(() => image.Frames[0].Threshold(new ThresholdOptions { Method = ThresholdMethod.Sauvola }, canceled.Token));
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.Threshold()));
        Assert.Equal([10, 20, 30], ReadGray8(image.Frames[0]));

        // The luminance of a frame is rented before a pixel changes: 2048 x 2048 pixels need 8 MiB
        var limits = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 6L * 1024 * 1024 } };
        using var limited = new Image<Gray8>(2048, 2048, new Gray8(100), limits);
        limited.Frames[0][0, 0] = new Gray8(200);
        Assert.Throws<ImageResourceLimitException>(() => limited.Threshold(new ThresholdOptions { Method = ThresholdMethod.Mean }, Ct));
        Assert.Equal(200, limited.Frames[0][0, 0].Value);
        Assert.Equal(100, limited.Frames[0][1, 0].Value);
        Assert.Equal(0, limited.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));

        // The global methods need one row
        limited.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed }, Ct);
        Assert.Equal(255, limited.Frames[0][0, 0].Value);
        Assert.Equal(0, limited.Frames[0][1, 0].Value);

        var disposed = new Image<Gray8>(2, 2);
        var frame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.Threshold());
        Assert.Throws<ObjectDisposedException>(() => frame.Threshold());
    }

    [Fact]
    public void FloatingPointPixelsAreRejected()
    {
        using var image = new Image<RgbaFloat>(2, 1, new RgbaFloat(0.25f, 0.5f, 0.75f, 1));
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed }, Ct));
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Frames[0].Threshold(cancellationToken: Ct));
        Assert.Equal(new RgbaFloat(0.25f, 0.5f, 0.75f, 1), image.Frames[0][1, 0]);

        using var half = new Image<RgbaHalf>(1, 1);
        Assert.Throws<UnsupportedImageFeatureException>(() => half.Threshold(new ThresholdOptions { Method = ThresholdMethod.Sauvola }, Ct));
    }

    #region Threshold_Sauvola
    [Fact]
    public void Threshold_Sauvola()
    {
        // A page lit unevenly: the paper goes from 120 (left) to 250 (right), and the dark strokes are 80 levels darker
        using var page = new Image<Gray8>(96, 32);
        page.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var paper = 120 + (x * 130 / 95);
                    var stroke = x % 8 < 2 && y is >= 8 and < 24;
                    row[x] = new Gray8((byte)(stroke ? paper - 80 : paper));
                }
            }
        });

        // No single level separates the strokes from the paper: the strokes on the right are lighter than the paper on the left
        Assert.True(page.Frames[0][88, 16].Value > page.Frames[0][4, 4].Value);

        // Sauvola's threshold follows the lighting: the strokes become black, the paper white
        page.Threshold(new ThresholdOptions { Method = ThresholdMethod.Sauvola, WindowSize = 25 });
        Assert.Equal(0, page.Frames[0][88, 16].Value);
        Assert.Equal(255, page.Frames[0][4, 4].Value);
        Assert.Equal(255, page.Frames[0][92, 16].Value);
    }
    #endregion

    private static int Pattern(int x, int y)
    {
        var value = 120 + (2 * x) - (y / 2) + (((x * 7) + (y * 13)) % 11);
        if ((x / 3) % 4 == 1 && y % 12 is >= 3 and <= 8)
        {
            value -= 80;
        }

        return Math.Clamp(value, 0, 255);
    }

    private static int Textured(int x, int y) => Math.Min(60 + (2 * x) + (((x * x * 7) + (y * y * 3) + (x * y * 5)) % 97), 254);

    private static byte OtsuFamily8(int seed, int x, int y) => (byte)(((x * x * (seed + 1)) + (y * (3 + seed)) + ((x * y) % (7 + seed))) % 256);

    private static Image<Gray8> CreateGray8(Func<int, int, int> value)
    {
        var image = new Image<Gray8>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                image.Frames[0][x, y] = new Gray8((byte)value(x, y));
            }
        }

        return image;
    }

    private static Image<Gray16> CreateGray16(Func<int, int, int> value)
    {
        var image = new Image<Gray16>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                image.Frames[0][x, y] = new Gray16((ushort)((value(x, y) * 257) + (((x * 31) + (y * 17)) % 257)));
            }
        }

        return image;
    }

    private static Image<Gray8> Gray8Row(params byte[] values)
    {
        var image = new Image<Gray8>(values.Length, 1);
        for (var x = 0; x < values.Length; x++)
        {
            image.Frames[0][x, 0] = new Gray8(values[x]);
        }

        return image;
    }

    private static byte[] ReadGray8(ImageFrame frame)
    {
        var typed = (ImageFrame<Gray8>)frame;
        var pixels = new Gray8[typed.Size.Width * typed.Size.Height];
        typed.CopyPixelDataTo(pixels);
        return [.. pixels.Select(pixel => pixel.Value)];
    }

    /// <summary>Frames [10, 20] and [30, 40], and a poster [50, 60].</summary>
    private static Image<Gray8> CreateTwoFrames()
    {
        var image = Gray8Row(10, 20);
        var second = image.AppendFrame();
        second[0, 0] = new Gray8(30);
        second[1, 0] = new Gray8(40);
        var poster = (ImageFrame<Gray8>)image.SetPosterFrame(image.Frames[0]);
        poster[0, 0] = new Gray8(50);
        poster[1, 0] = new Gray8(60);
        return image;
    }

    private static void AssertMask(string expected, bool[] actual)
    {
        var bits = Convert.FromBase64String(expected);
        var mismatches = new List<string>();
        for (var i = 0; i < actual.Length; i++)
        {
            var bit = (bits[i / 8] & (0x80 >> (i % 8))) != 0;
            if (bit != actual[i])
            {
                mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"({i % Width}, {i / Width})"));
            }
        }

        Assert.Empty(mismatches);
    }
}
