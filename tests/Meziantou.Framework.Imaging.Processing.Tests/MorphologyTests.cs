namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Morphological filters (erosion, dilation, opening, closing, top-hat transforms) and the percentile filter. The
/// expectations are hand-computed, computed by the brute-force definition of the filters (<see cref="Reference"/>), or come
/// from SciPy 1.18.1 (<c>ndimage.grey_erosion</c>, <c>grey_dilation</c> and <c>percentile_filter</c>, whose modes
/// <c>nearest</c>, <c>reflect</c>, <c>wrap</c> and <c>constant</c> are the edge modes <c>Clamp</c>, <c>Mirror</c>,
/// <c>Wrap</c> and <c>Zero</c>), scikit-image 0.26.0 (<c>morphology.disk</c>) and OpenCV 5.0.0
/// (<c>cv2.morphologyEx</c> with <c>BORDER_REPLICATE</c>, and <c>cv2.erode</c> with its default border), run in a
/// <c>python:3.13-slim</c> container (numpy 2.5.4) on <see cref="CreateNoise"/>, and kept as literals (base64 of the
/// 8-bit samples).
/// </summary>
public sealed class MorphologyTests
{
    private const int Width = 24;
    private const int Height = 17;

    private static readonly Dictionary<string, string> References = new(StringComparer.Ordinal)
    {
        ["erode_rect_2_1_nearest"] = "BAQEAgICAgIdGxsbExMICAgICBAQEBAQBAQEAgICAgIGBhsbExMICAgICAkJCQkQAgICAgICAgIGBi0tLjUICAgEBAQEBAkQAgICEREGBgYGBiYmEhINDQ0EBAQEBAk2AgICCAgICAgtCgoKCgoNDQ0EBAQEBBU2GBgYCAgICAgyCgoKCgoNDQ0NDRURERERGBgYCAgICAgyCgoKCgoQBAQEBAQRERERGBgYEhISEhIyGxYQAgICAgIEBAQRERERKSkpEhISEhIbGwsLAgICAgIEBAQaFRUVIyMjIwcHBwcHGwsLAgICAgIFBQoKCgoKIyMjIwcHBwcHCQkJCwsLMxwcHAoKCgoKIyMjIwcHBwcHCQkJGxspKRwcHAoKCgoKQzg4CAgICAgJCAgICAgpEhISEhIkJCR3QwQEBAQECAgOCAgICAgMEgkJCQkJJCRYQwQEBAQECAgSCAgICAgMDAkJCQkJExMTVwQEBAQEEhISDAwMDAAAAAAACQkJDw8TV0dHR0cSEhISDAwMDAAAAAAADAwPDw8T",
        ["dilate_disk3_reflect"] = "3Nzc3P//////8vL5+f39/f3/////////3Nzc6v//////8vL5+f39/f39///////S3Or0//////////n5/f39/f39///////83Or79P//////8/j5+f39/f39/fr//Pz8+/v7+///////9vj5+f39/f39+vr6/Pz8+/v7+/v2//b2+Pj4+Pn4/fr6+vr8/Pz8+/v7+/v79vb28/j4+Pj4+Pj6+vz8/Pz8+/v7+/vg9vPz//j4+Pj4+Pj8/Pz8/Pz8+/v7+/vr6///////+Pf39/f8/Pz8/Pz83uD76+vr6///////9/f39/z8/Pz8/Pz83uvr6+vr//////////f39/f8/Pz8/Pz83t7r6+vr6///////9/f39/f8/Pz8/Ovl6d7r6+vr6///////9/f39/r19fzp6eXl6enp0+vd9vb2//b29v76+vr6+vXk4uLl6enp5OTk5Pb29vb+/v7+/vr6+vX14uLi6enp6eTk5Pb29vb+/v7+/vr6+vre4uLi6enp5OTk5OTd9v7+/v7+/v76+vXy1tHi",
        ["erode_ell_4_2_wrap"] = "BAQCAgICAgICGxMMAAAAAAAAAAgJBAQEAgICAgICAgICAgYTCAgIAAgICAQIBAQEAgICAgICAgICBgYGHggICAQEBAQEAgICAgICAgICBgYGBgYKEg0NBAQEBAQCAgICAgICAggICAYICgoKCgoKDQQEBAQEAgICAggICAgICAgICAoKCgoKCg0EDQQREREREREICAgICAgICgoKCgoCBAQEBAQEERERERESEhIIEhISEhACAgICAgICBAQEBBERFRUSEhISBxISCwICAgICAgICAgQEChEVChUVBwcHBwcHBwsCAgICAgICBQUKCgoKCgoHBwcHBwcHBwcJCQsCCwUKCgoKCgoKCiMjBwcHBwcHBwkICQkbHBwSCgoKCgoKOCMIBAgIBwgICAgICAgIEhISCRISCiQkBAQEBAQEBAgICAgICAgICAkJCQkJCRMkBAQEBAQEBAQICAgICAgIAAkJCQkJCQkEBAQEBAQEBAwMDAwIAAAAAAAAAAkJCQ8PBAQEBAQCEhIMDAwAAAAAAAAAAAAMDwQE",
        ["erode_rect_1_2_constant"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIEHQICAgYGLRsbGxISCAgIBAQECRAAAAIEHQICAgYGLQoKChISCAgIBAQECRAAAAJcKQgIBgYGLQoKChAQDQ0NBAQECREAAAJcXAgICDItLQoKChAQDQQEBAQEEREAABgqKggICDIyMgoKCgICAgQEBBUVEREAABgqKggICB4eGwoKCgICAgQEBBoaEREAABgjKhIHBwceGxsLCwICAgQEBBoKCgoAACMjKhIHBwcJCQkLCwICAgQEBBoKCgoAACMjKigHBwcJCQkLCwICAgUFHBoKCgoAACMjOAgHBwcJCQgICAstKRISEhwKCgoAACMEBAQHBwcJCQgICAwtKRIJCQkKCgoAAEMEBAQICA4JCQgICAwtKQwJCQkTExMAAEMEBAQICA4ODggICAwAAAAJCQkPDxMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        ["dilate_rect_3_0_reflect"] = "3Nzc3K3a5eXl5eXl5ax5/f3/////////tLS0tLS0tHjy8vLy8vLy3+7u7u7u7u7LzMzM//////////n5/f39/f39/crKysqzlrPq6urq6urq1NTg5ubm5ubn5+fn5+fn6vT09vb29vb29uvr6+Tk5NXV1cvLs3dq29vb29vz8/Pz+Pj4+Pj4+Pr6+vr8/Pz8+/v7+/v7urq6tKWl9/f39/f3+Pj4+Pj4lcvLy8vOzvHx8fHx8fHX19fXvPHx8fHxxMTExMTExMfHx8fHx8etO5mZ/Pz8/Pz83uDg4ODg4ODW1ta+vr7T0/z8/Pz8/Pzfl+vr6+vr//////////f39/f37u7p6enl3t7e3tfX19fn5+fn5+fn4sTE1tbW1tbW3Nzc3NHR0b6+vqqqqorm5ubm5ubm39/cu7u7u9bW9vb29vb29tnZ2dnZycni4uLi2tra2rKynp6amt3d3d319fX19fX10dHR6enp6d3d3d3d3aevr/r6+vr6+vre3tbOy+Tk5OTk5OTV1f7+/v7+/v7y8vLysbGD",
        ["open_rect_2_1"] = "BAQEBAQCHR0dHR0bGxsTEwgQEBAQEBAQBAQEBAQCHR0tLS41NTU1NQgQEBAQEBAQBBEREREREQYtLS41NTU1NQ0NDQkJNjY2AhERERERLS0tLS41NTU1NQ0NDQQVNjY2GBgYGBgRMjIyMjImJiYSEg0VFRUVNjY2GBgYGBgIMjIyMjIKEBAQEBAVFRUVNjY2GBgYGBgSMjIyMjIbFhAQEBAVFRUVFRERKSkpKSkSMjIyMjIbFhAQEBAEGhoaGhoVKSkpKSkjMjIyMjIbFhACBQUKGhoaGhoVKSkpKSkjGxsbGxsbCzMzMzMzHBwcGhoVIyMjIyMjBxsbGxsbKTMzMzMzHBwcCgoKQ0NDODgjCQkJCRsbKTMzMzMzJCQkd3d3Q0NDODgjDg4ODhsbKSkpKSkpJCQkd3d3Q0NDODgIEhISEhIIKSkpKSkSJCQkd3d3V1dXBBISEhISEhIMDBISEhISCSQkWFhYV1dXR0dHRxISEhIMDAwMDAwMDxMTExMTV1dXR0dHRxISEhIMDAwMAAwMDw8PExMT",
        ["open_disk2"] = "BAQEEQQGHR0tLS0bHjUTExMQEBAQGRk2BAQRKRECBi0tLS0tNTU1EwgQEBAQEDY2BBEsXCkRCAYtLS01NTU1NRINEBAJNjY2AixcXFwpES0yLS0tNTU1EhINJAQ2NjY2LFxcXFxcLTIyMi0mJjUSEg0kJCQVNjY2XFxcXFwIMjIyMjIKJhISECQkJCQkFTY2GFxcXCoyMjIyMjIbEBAQEBAkJCQkJBE2KSpcKioSMjIyMjIyFhAQEBAEJCQkGhoVKioqKioqHjIyMjIbGxACHwUcHCQaGhoVIyoqOCoqKh4eMhsbCzMzKSkcHBwcGhVoRCM4ODgqBx4eGxsLMzMzMykpHCQkCmh3REQ4ODg4KA4bCQszMzMzMzMpKSQkaHd+REREODgoDhIOMgkbMzMzMykpJCRVd35+RFdDQygIEhIyMjIILTMzLS0SJCQkfn5+V1dXBEcgEjIyMjIyDC0tLRISCSQkJH5+V1dXV0dHIBIyMjIMDAwtEhIJDw8TJBN+V1dXR0dHRzIyMjIyDAwMAAkPDw8PExMT",
        ["close_rect_2_1"] = "3LS0tLS0tLTa5eXy37e3t7e3/f3/y8vLzLS0tLS0tLTa5eXy37e3t7e37u7ny8vLzMzMzMzM/+vr6+vr6/n5/efn5+fny8vLycnJzMzq9uvr6+vr6/j4+Ofn5+fn0tLSycnJ6vT09uvr6+vr6/j4+Ofn5+fn0tLSycnJ29vb29vz8/Pz+Pj4+Pj4+Pr6+vr8+/v7y8vLy8vOzvHx8fHx9+Li4uLi+vr83t7ey8vLy8vOzvHX19fX19fX1+Li+vr83t7ey8vLy8vOzvHX19fX19fX1+Li/Pz83t7e3uDW1tbW1vHX19fX19fX1/zl5eXl3t7e3uvr6+vr//f39/f37u7p6enl5eXl3tHR0dHR0dfX9ufn4uLi4uLm39/f39/i3NHR0dHR0dbW9t3d3d3d3d3m39/f39/i3NHR0dHR0dbW9t3d3d3d3d3m3t7e3t7i3NHR0dHR0dXV1dXV1d3d3d313tbR0dHR6dPT09PT3dXV1dXV1d3d3fry3tbOzs7O6eTk5OTd3dXV1dXV1f76+vry3tbOzs7O",
        ["close_disk2"] = "3LS0ra2tra3l5eXlt7e3t7f9/e7/y8vLtcy0tK2treXl5eXy8re3t/345+7Ly8vLtbXMtLSt/+Xl5evr8vm3/fjn5+fSy8vLtbW1zLT08+vl6+vr6+bm5ufn5+fn0svLtbXJ6vTL9vPr6+vr5ubm5ubn5+fn59LLtbq69MvLy9bz6+vr+Nfm5vf44vrn8uf8urr7y8vLy8vW8evx1NfX9+7W4uL6+Pr8ut7ey8vLy8vO1vHU1NTX19bW1uLi+vzr3t7e3svLy87W1tTU1NTU1tbW1tb6/Ovl3t7e3uDLztbW1uLU1NTX7tbW1vzp5OXl3t7c0evd1tbW/+fi1Nf35u7W5unk3+Ll3tzR0dHW19fX1+fn4t3d2ebm5t/f39/i3NHR0dHR1tfX1+fd3d3Z2dnm39/W0d/f0dHR0dHR0dbX9tXV3dnZ2dnZ39bR0dHi2tHR0dHR1tfR1dXV1d3Z2dn11tHRztGX6drR0dHW3dHR0dXV1dXd2fre3tbOzpeX5OTa0eTd0dHR0dHV1f7d+vLy1s7Ol5eX",
        ["tophat_rect_2_1"] = "2ABhphurAD29yI8AA0QAXXFc7QDvCZ9QGQCosBkADilLRgy9qip5ggBJwd4pALs4kaS7eBgA7gCJNQCqB8QAyD6HG8EARg59AE2FTqLZQH+nAFM5NHqrsXo/jwDSnAAnFEix0txoxFRuuWEAvjwAwwC2nmIANDQEUKJ2w1wAFGzBnIEA6MDNWKvjSuUPvJzGAG/jRG+oggYAc2kAJwBs52jBas1Y4wCAAFVqbKIAkyWcKL+5AAjHCawAQWR/S9eNa0ibAYNHdwaSQ5WSHCgAGgAxEn8A4tEAtaY/kbdZobsDfAB/AGqLZDCgduCTxYh3dAAiP8i6AEpV5D1uQZzEl7sMAM3IAF7bm1BKAGRatM5SALnMuQARDBdZoH1sX0FBmR4cmVYFsEsAnDUAJ2FBDQC9TLsxAENlASoAg3EABcSu5GkAOEewBKAATjcAB0trg1pbAIwObmIliF2EAMscTGDjACSSAHk/kmw1aieMlgAyUCAACJujM+4Az8MAuwBzdABJAJ0Ud78zoiTJfPLGADXmeKIAcExh",
        ["tophat_disk2"] = "2ABhmRunAD2tuH8AACoAXWZc7QDvAJYqGQCbiwwAJRlLRg3Fqip5pABJwd4pAJUSkaSgLQAA9wCJNQGqB8QAyDmHGLoARg59ADI6A1fBXH+iAFRBNHqr1HU/eACxnAAnAARtjpgdyVRuuWYAvi0AwwCnj1MANDQEDF4yfxgAFGzBnIEA0r7LWJfUO9YA3ZzGACufAF2IggYAc2kALQBs52iyW75J1ABbAFQ3a6EAkyWcKL+iAAjHCawAN1p1S9eNakeaAIJAiwaSQ5WSFygAAAAfEHUA4tEAu6U+grZSkrgAZQB/AGqLbjq3duCTxY0kUwANKrOzAEdS5D1+N5zEl8UWAMXAAABumk9VAGRFlclAAMm0rwARDBdjm31sbkE6mB0bmVYAsEcAeEcAHVc3AwC9TLsAADxeABYAeIEABcSOxEkAND2mAJwATjcAAERkg1pbAFcAbkIFaD1eALABMWDjACSSNFMZkmw1WieMvQASMAAACJuCLegDz8cAqgAIdABJAJ0Ud58TggSjfPLGADjjeKIAcExh",
        ["blackhat_rect_2_1"] = "ALBPCpUHl1oAADnXwVikRz5LAO0Ashxrr7AIAJeyiW5icqsAAFgJAK9eHQCuuwCDNxcAQ6O7AOU1ib0MrwDEAJxTvx3eT4cYx2szbRkAiT8Xvmp9gkkYEmCbS+MAAJx1nWkAAAB7AGVLAFjFB5bmI9ocNHDSaGiYYQ87AGfTlT0AJUDpACgbkD0AmQDWCCgA43QAb0QRF5OcKVbWtOF1AGoMYwB1AulrtWBLNgC5BnQAdAADwb8AvhvTfGRJlQlaSm0aoR9hIpMKWSoqpZ/VuNKcq0nIABHnAA92JABaGgC4P9Y9zDoZQHQERQA2BkNZR7uZfAAO5IZ7AJ9ujSgAJACqzQAB230AAD5EmTVUFAB87RMAAK+eo5haGz5PCScqAHByAEOpE33ITI3CjVNzp7QAbwCKaCUGmGSOFijJugAWAGLVfG0EsBTUbIO6YBwAAiAfzTOxUWGeO2ZFyQCvf2sA1Y4beQA6ABBHImUAAMORc6PJwTYungDmAAC7ALtIHo1EnQCCHwSQIZ8ATQAo+rkAVyW/S29a",
        ["blackhat_disk2"] = "ALBPA44AkFMLADnKmVikRz6RAN4AshxrmMgIAJCrgp9tcqsAE1gJAPWfFgCSuwCDIAAAK4ucAN8vg70MtgCCAK1Tvx3JT4cYs1cfbQEKhj8Rvmp9gjcGAGCbS+MAAJVuiVUAAABSAG1LAFjFAoTUEdkcNHDSfWiRTQAsGVfDhTgAHTjhAAcJfjwAgwDDABUAojMAb0QRF5OkTFDWl8dbAHYAYwCNAOlrkWBLNgC5BnQAfAAAvrwAvhrSe2RJlQtJSm0atB9hIpYSYQ0nopzSt9Gbqj3gAADQAA92JABPEgC4P8c6yTcZV3MDRAA6BUNZR7uXbwAAz3FmAI9ZaggAHACXygAA1XoAAElEmTVZGgB8zhMAAKqZmpxaIj5PCScqAHByAEOpGH7JLZfCjVNvo7AAbwCBWiUDjWSOFijJugAXAFrNfGkArBDHbXutUw8AACAfzTOxVmOaO2ZFyQCre2cAzYkbdgAAABdFIGMDAL+Nb6PJwS4umgDSAAC7AIQRGY06igCCEwCMHZsATQAL+rEATx2/FDgj",
        ["cv_erode_disk2_default"] = "BAQEBAICAgYdGxsbExMTCAgIEBAJEBAZAgQEAgICAgIGLRsbGxMICAgICAQJCRAQAgIEEQICAgYGBi0bHjUSCAgIBAQECQk2AgICKRECBgYGLSYKJhISDQgEBAQEBDY2AgIsXAgICAYtJgoKChANDQ0NBAQEFRE2AhhcCAgICAgyCgoKCgoQDQ0EJAQVERERGBgYKggICDIyGwoKChACEAQEBCQRERERGBgpEhIIEhIeMhYKCwICAgQEBAQaERERGCMqKhISBx4eGxsLAgICAgIEBBoaChEVIyMjKioHBwcbCQsLCwICAgUEHBoKCgoVIyMjIwcHBwcHCQkLCwsCHwUcHAoKCgoKIyMjOCgHBwcJCQkICzMzKSkSHBwKCgpoRCM4BAgIBw4JCQgICBspKRISCSQkClV3Q0MEBAQICAgOCAgICAgtEhIJCQkTJBN+RAQEBAQECBIOMggICAwMAAkJCQkJExMTRFcEBAQIEhISDAwIDAwAAAAJCQkPDxMTV0dHBEcgEhISMgwMDAAAAAAACQ8PDw8T",
        ["percentile_2_25_reflect"] = "BB0dER0dHSs6Rjo6LjU1WUtZORkZGTlIBB0fHx8fHy06Rjo6LjU8WUxMORkZGUREHSwpKSspKUZGRjw8NTw8TEtMKCgoNjpELCxeXkYrK0ZGRmJfPGJfWUxZKDk2OTpEXlxfYG1cRkZiMjw8NTxLYl9oS182OjpELCxfdHRfbVdtVz0tJhs9TF9fW19fXV1dYCxocXRXV1d1Vz0yGxkYGB87LFtfZTplaGhxcWpXRkZXODIbGxkZHztbW19lfm2MRUViaGhcV1dXVzIyGxsZHyw7Pz9lbWhocWJoaGhiZVpYWlgzMzIfMyw7Pz9ojIyMaGJiYWJiW1lQUDI4ODYzODY7P1VwjHeMX19hX0ViWShQUB5QUD9EPz8/VVtwfoyMX0VfQzg4NyhYWFBQUDY/PzY/SEhYaH6XbWFtX0M4NyhEMhsyMi42NjY/SFVYW36GYV9hV0dHNzdENjI2Mi02Ni42KUhVWF90V1dXV0dHRUVENzY2Ni0/Py4/JCRIWF9foIxuW25bRUU3NjIyMhQ/Pz8/ExMTSFhf",
        ["percentile_1_90_nearest"] = "3Ny0tLStrdrl5fLy8t+3t7f9/f///8vL3NzMzLT////l5fLy+fn9/f39/f///8vLtczMzOr////U1PLy+fn9/f3R7u7u59LLtczq9PT////r6+vk+fn9/f3Ly+fn59Kzusnq9PT29vbz8+v4+Pjm5vj4+vr68vz8uvv7+/T29vbz8+v4+Pj39/j4+vr6+Pz8uvv7+9vLxfPz8/H4+Pj39/j4+vr6+Pz8lPv7+8vLxc7O8fHx1Nf39/fW4uL8/Pzx3t7P4ODg1tbW8fHx1NfX19PT/Pz8/Pzx3t7P6+vr3db////Hz/f39+7u/Pz8/Pzr3t7P6+vr3df////n5/f39+7u/Pz85OXl3t7R6+vr3df////n5/f39+7u6enp5OXl3t7R0dG+19f29vbn5+LZ2ebm5t/f1uLi3NzR0dG+1tb29vaQ3d3d2fX19d/f0eLi6enDu9Pd3d329vaQ3d3d+vr69d7W0eLi6enD5OTk3d3RtNXV/v7++vr69d7W0dHR6enD5OTk3d3RtNXV/v7++vr68t7Wzs6G",
        ["percentile_3_100_wrap"] = "6enq//////////7+/v7+/v7/////////6vT0//////////7+/v7+/v7//////////Pz8//////////7+/v7+/v7//////////Pz8//////////n5/f39/f3//////////Pz8//////////n5/f39/f39/fr8/Pz8/Pz8//////////n5/f39/f39/fz8/Pz8/Pz8+/v79vb2+Pj4+Pj4+Pz8/Pz8/Pz8/Pz8+/v7//////////j4+Pz8/Pz8/Pz8/Pz8+/v7//////////j4+Pz8/Pz8/Pz8/Pv7+/v7//////////f39/z8/Pz8/Pz8/PHr6+vr//////////f39/z8/Pz8/Pz8/Ovr6+vr//////////f39/z8/Pz8/Pz86evr6+vr//////////r6+vz8/Pz8/Pzp6evr6+vr//////////7+/v76+vr16enp6enp6eTk9vb29v7+/v7+/v7/////////6enp6eTk9vb29v7+/v7+/v7/////////6enp//////////7+/v7+/v7/////////",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    #region Morphology_RemoveSpecks
    [Fact]
    public void Morphology_RemoveSpecks()
    {
        // A dark page with a bright one-pixel speck and a bright 5x5 square
        using var image = new Image<Gray8>(16, 16, new Gray8(20));
        image.Frames[0][2, 2] = new Gray8(250);
        for (var y = 8; y < 13; y++)
        {
            for (var x = 8; x < 13; x++)
            {
                image.Frames[0][x, y] = new Gray8(200);
            }
        }

        // An opening by a 3x3 square removes what is smaller than the square, and keeps the larger square intact
        image.MorphologicalOpen(StructuringElement.CreateRectangle(1, 1));

        Assert.Equal(20, image.Frames[0][2, 2].Value);
        Assert.Equal(200, image.Frames[0][8, 8].Value);
        Assert.Equal(200, image.Frames[0][12, 12].Value);
        Assert.Equal(20, image.Frames[0][13, 13].Value);
    }
    #endregion

    [Theory]
    [InlineData("erode_rect_2_1_nearest")]
    [InlineData("dilate_disk3_reflect")]
    [InlineData("erode_ell_4_2_wrap")]
    [InlineData("erode_rect_1_2_constant")]
    [InlineData("dilate_rect_3_0_reflect")]
    [InlineData("open_rect_2_1")]
    [InlineData("open_disk2")]
    [InlineData("close_rect_2_1")]
    [InlineData("close_disk2")]
    [InlineData("tophat_rect_2_1")]
    [InlineData("tophat_disk2")]
    [InlineData("blackhat_rect_2_1")]
    [InlineData("blackhat_disk2")]
    [InlineData("cv_erode_disk2_default")]
    [InlineData("percentile_2_25_reflect")]
    [InlineData("percentile_1_90_nearest")]
    [InlineData("percentile_3_100_wrap")]
    public void Morphology_MatchesScipyAndOpenCV(string name)
    {
        using var image = CreateNoise();
        var rectangle = StructuringElement.CreateRectangle(2, 1);
        var disk2 = StructuringElement.CreateEllipse(2, 2);
        switch (name)
        {
            case "erode_rect_2_1_nearest":
                image.Erode(rectangle, ConvolutionEdgeMode.Clamp, Ct);
                break;
            case "dilate_disk3_reflect":
                image.Dilate(StructuringElement.CreateEllipse(3, 3), ConvolutionEdgeMode.Mirror, Ct);
                break;
            case "erode_ell_4_2_wrap":
                image.Erode(StructuringElement.CreateEllipse(4, 2), ConvolutionEdgeMode.Wrap, Ct);
                break;
            case "erode_rect_1_2_constant":
                image.Erode(StructuringElement.CreateRectangle(1, 2), ConvolutionEdgeMode.Zero, Ct);
                break;
            case "dilate_rect_3_0_reflect":
                image.Dilate(StructuringElement.CreateRectangle(3, 0), ConvolutionEdgeMode.Mirror, Ct);
                break;
            case "open_rect_2_1":
                image.MorphologicalOpen(rectangle, cancellationToken: Ct);
                break;
            case "open_disk2":
                image.MorphologicalOpen(disk2, cancellationToken: Ct);
                break;
            case "close_rect_2_1":
                image.MorphologicalClose(rectangle, cancellationToken: Ct);
                break;
            case "close_disk2":
                image.MorphologicalClose(disk2, cancellationToken: Ct);
                break;
            case "tophat_rect_2_1":
                image.TopHat(rectangle, cancellationToken: Ct);
                break;
            case "tophat_disk2":
                image.TopHat(disk2, cancellationToken: Ct);
                break;
            case "blackhat_rect_2_1":
                image.BlackHat(rectangle, cancellationToken: Ct);
                break;
            case "blackhat_disk2":
                image.BlackHat(disk2, cancellationToken: Ct);
                break;
            case "cv_erode_disk2_default":
                // OpenCV ignores the pixels outside the image by default: the result of Clamp
                image.Erode(disk2, cancellationToken: Ct);
                break;
            case "percentile_2_25_reflect":
                image.PercentileFilter(2, 25, ConvolutionEdgeMode.Mirror, Ct);
                break;
            case "percentile_1_90_nearest":
                image.PercentileFilter(1, 90, ConvolutionEdgeMode.Clamp, Ct);
                break;
            default:
                image.PercentileFilter(3, 100, ConvolutionEdgeMode.Wrap, Ct);
                break;
        }

        Assert.Equal(References[name], Convert.ToBase64String(ReadGray8(image)));
    }

    [Fact]
    public void Ellipse_IsTheDiskOfScikitImage()
    {
        // morphology.disk(3), row by row
        string[] disk =
        [
            "...#...",
            ".#####.",
            ".#####.",
            "#######",
            ".#####.",
            ".#####.",
            "...#...",
        ];
        var element = StructuringElement.CreateEllipse(3, 3);
        Assert.Equal(7, element.Width);
        Assert.Equal(7, element.Height);
        for (var dy = -3; dy <= 3; dy++)
        {
            for (var dx = -3; dx <= 3; dx++)
            {
                Assert.Equal(disk[dy + 3][dx + 3] == '#', element.Contains(dx, dy));
            }
        }

        // A radius of 0 gives a line, and the rows of an ellipse have the exact half-widths
        var line = StructuringElement.CreateEllipse(0, 2);
        Assert.True(line.Contains(0, 2));
        Assert.False(line.Contains(1, 0));
        var ellipse = StructuringElement.CreateEllipse(5, 3);
        Assert.True(ellipse.Contains(5, 0));
        Assert.False(ellipse.Contains(5, 1));
        Assert.True(ellipse.Contains(4, 1));
        Assert.True(ellipse.Contains(0, 3));
        Assert.False(ellipse.Contains(1, 3));
        Assert.False(ellipse.Contains(0, 4));
        Assert.True(StructuringElement.CreateRectangle(2, 1).Contains(-2, 1));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Morphology_MatchesTheBruteForceDefinition()
    {
        var random = new Random(3767);
        ConvolutionEdgeMode[] modes = [ConvolutionEdgeMode.Clamp, ConvolutionEdgeMode.Mirror, ConvolutionEdgeMode.Wrap, ConvolutionEdgeMode.Zero];
        for (var iteration = 0; iteration < 160; iteration++)
        {
            var width = random.Next(1, 20);
            var height = random.Next(1, 20);
            var rx = random.Next(0, 7);
            var ry = random.Next(0, 7);
            var element = random.Next(2) == 0 ? StructuringElement.CreateRectangle(rx, ry) : StructuringElement.CreateEllipse(rx, ry);
            var mode = modes[random.Next(modes.Length)];
            var dilate = random.Next(2) == 0;
            var samples = new byte[width * height * 4];
            random.NextBytes(samples);

            using var image = new Image<Rgba32>(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = ((y * width) + x) * 4;
                    image.Frames[0][x, y] = new Rgba32(samples[i], samples[i + 1], samples[i + 2], samples[i + 3]);
                }
            }

            if (dilate)
            {
                image.Dilate(element, mode, Ct);
            }
            else
            {
                image.Erode(element, mode, Ct);
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var pixel = image.Frames[0][x, y];
                    byte[] actual = [pixel.R, pixel.G, pixel.B, pixel.A];
                    for (var c = 0; c < 4; c++)
                    {
                        var expected = Reference(samples, width, height, 4, c, x, y, element, mode, dilate);
                        if (expected != actual[c])
                        {
                            Assert.Fail($"Iteration {iteration}: {width}x{height}, rx={rx} ry={ry} {mode} dilate={dilate}, pixel ({x}, {y}) channel {c}: expected {expected}, got {actual[c]}.");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void Morphology_CommutesWithTheSampleFormats()
    {
        // An erosion keeps the order of the samples: it commutes with every increasing map of the levels
        var element = StructuringElement.CreateEllipse(3, 2);
        using var reference = CreateNoise();
        reference.Erode(element, ConvolutionEdgeMode.Mirror, Ct);
        var expected = ReadGray8(reference);

        using var noise = CreateNoise();
        var levels = ReadGray8(noise);
        using var gray16 = new Image<Gray16>(Width, Height);
        using var rgb48 = new Image<Rgb48>(Width, Height);
        using var floats = new Image<RgbaFloat>(Width, Height);
        using var halves = new Image<RgbaHalf>(Width, Height);
        using var la16 = new Image<La16>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var v = levels[(y * Width) + x];
                gray16.Frames[0][x, y] = new Gray16((ushort)((v * 256) + 3));
                rgb48.Frames[0][x, y] = new Rgb48((ushort)(v * 256), (ushort)(65535 - (v * 256)), 7);
                floats.Frames[0][x, y] = new RgbaFloat((v - 128) / 7f, -v * 1e30f, v == 0 ? -0f : v, 1);
                halves.Frames[0][x, y] = new RgbaHalf((Half)(v - 100), (Half)0, (Half)1, (Half)(v / 255f));
                la16.Frames[0][x, y] = new La16(v, (byte)(255 - v));
            }
        }

        gray16.Erode(element, ConvolutionEdgeMode.Mirror, Ct);
        rgb48.Erode(element, ConvolutionEdgeMode.Mirror, Ct);
        floats.Erode(element, ConvolutionEdgeMode.Mirror, Ct);
        halves.Erode(element, ConvolutionEdgeMode.Mirror, Ct);
        la16.Dilate(element, ConvolutionEdgeMode.Mirror, Ct);
        using var dilated = CreateNoise();
        dilated.Dilate(element, ConvolutionEdgeMode.Mirror, Ct);
        var expectedDilated = ReadGray8(dilated);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var v = expected[(y * Width) + x];
                var max = expectedDilated[(y * Width) + x];
                Assert.Equal((ushort)((v * 256) + 3), gray16.Frames[0][x, y].Value);
                Assert.Equal(new Rgb48((ushort)(v * 256), (ushort)(65535 - (max * 256)), 7), rgb48.Frames[0][x, y]);
                var f = floats.Frames[0][x, y];
                Assert.Equal((v - 128) / 7f, f.R);
                Assert.Equal(-max * 1e30f, f.G);
                Assert.Equal(v == 0 ? -0f : v, f.B);
                Assert.True(v != 0 || float.IsNegative(f.B));
                Assert.Equal(new RgbaHalf((Half)(v - 100), (Half)0, (Half)1, (Half)(v / 255f)), halves.Frames[0][x, y]);

                // The alpha of a gray format is filtered too: the dilation of 255 - v is 255 minus the erosion of v
                Assert.Equal(new La16(max, (byte)(255 - v)), la16.Frames[0][x, y]);
            }
        }
    }

    [Fact]
    public void Morphology_OrdersFloatingPointSamplesAsNumbers()
    {
        using var image = new Image<RgbaFloat>(3, 1);
        image.Frames[0][0, 0] = new RgbaFloat(float.NaN, 0f, float.NegativeInfinity, 1);
        image.Frames[0][1, 0] = new RgbaFloat(5, -0f, float.PositiveInfinity, 1);
        image.Frames[0][2, 0] = new RgbaFloat(-5, 0f, 3, 1);
        using var dilated = image.Clone();
        image.Erode(StructuringElement.CreateRectangle(1, 0), cancellationToken: Ct);
        dilated.Dilate(StructuringElement.CreateRectangle(1, 0), cancellationToken: Ct);

        // A number that is not a number is above the positive infinity, and -0 is below +0
        Assert.Equal(new RgbaFloat(5, -0f, float.NegativeInfinity, 1), image.Frames[0][0, 0]);
        Assert.True(float.IsNegative(image.Frames[0][0, 0].G));
        Assert.Equal(new RgbaFloat(-5, -0f, float.NegativeInfinity, 1), image.Frames[0][1, 0]);
        Assert.True(float.IsNaN(dilated.Frames[0][0, 0].R));
        Assert.True(float.IsNaN(dilated.Frames[0][1, 0].R));
        Assert.Equal(5, dilated.Frames[0][2, 0].R);
        Assert.False(float.IsNegative(dilated.Frames[0][1, 0].G));
        Assert.Equal(float.PositiveInfinity, dilated.Frames[0][2, 0].B);
    }

    [Fact]
    public void TopHat_KeepsAlpha()
    {
        using var image = new Image<Rgba32>(5, 1, new Rgba32(10, 20, 30, 77));
        image.Frames[0][2, 0] = new Rgba32(200, 20, 5, 9);
        using var black = image.Clone();
        image.TopHat(StructuringElement.CreateRectangle(1, 0), cancellationToken: Ct);
        Assert.Equal(new Rgba32(190, 0, 0, 9), image.Frames[0][2, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 77), image.Frames[0][0, 0]);

        black.BlackHat(StructuringElement.CreateRectangle(1, 0), cancellationToken: Ct);
        Assert.Equal(new Rgba32(0, 0, 25, 9), black.Frames[0][2, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 77), black.Frames[0][4, 0]);

        // Floating-point differences are not clamped
        using var floats = new Image<RgbaFloat>(3, 1, new RgbaFloat(1, 2, 3, 0.5f));
        floats.Frames[0][1, 0] = new RgbaFloat(4, 2, -1, 0.25f);
        floats.TopHat(StructuringElement.CreateRectangle(1, 0), cancellationToken: Ct);
        Assert.Equal(new RgbaFloat(3, 0, 0, 0.25f), floats.Frames[0][1, 0]);

        using var mask = new Image<A8>(3, 3);
        Assert.Throws<NotSupportedException>(() => mask.TopHat(StructuringElement.CreateRectangle(1, 1)));
        Assert.Throws<NotSupportedException>(() => mask.Frames[0].BlackHat(StructuringElement.CreateRectangle(1, 1)));
        mask.Erode(StructuringElement.CreateRectangle(1, 1), cancellationToken: Ct);
    }

    [Fact]
    public void PercentileFilter_ExtremesAreTheErosionAndTheDilationOfASquare()
    {
        foreach (var mode in new[] { ConvolutionEdgeMode.Clamp, ConvolutionEdgeMode.Zero, ConvolutionEdgeMode.Wrap })
        {
            using var minimum = CreateNoise();
            using var eroded = CreateNoise();
            minimum.PercentileFilter(2, 0, mode, Ct);
            eroded.Erode(StructuringElement.CreateRectangle(2, 2), mode, Ct);
            Assert.Equal(ReadGray8(eroded), ReadGray8(minimum));

            using var maximum = CreateNoise();
            using var dilated = CreateNoise();
            maximum.PercentileFilter(2, 100, mode, Ct);
            dilated.Dilate(StructuringElement.CreateRectangle(2, 2), mode, Ct);
            Assert.Equal(ReadGray8(dilated), ReadGray8(maximum));

            using var median = CreateNoise();
            using var percentile = CreateNoise();
            median.MedianBlur(2, mode, Ct);
            percentile.PercentileFilter(2, 50, mode, Ct);
            Assert.Equal(ReadGray8(median), ReadGray8(percentile));
        }

        // Single-precision samples of a radius of 1 are sorted rather than counted
        using var floats = new Image<RgbaFloat>(3, 3);
        for (var i = 0; i < 9; i++)
        {
            floats.Frames[0][i % 3, i / 3] = new RgbaFloat(i, -i, i * 0.5f, 1);
        }

        floats.PercentileFilter(1, 75, cancellationToken: Ct);
        Assert.Equal(new RgbaFloat(6, -2, 3, 1), floats.Frames[0][1, 1]);
    }

    [Fact]
    public void Morphology_FramesScratchAndCancellation()
    {
        using var image = new Image<Gray8>(6, 3, new Gray8(9));
        image.Frames[0][3, 1] = new Gray8(0);
        image.AppendFrame()[3, 1] = new Gray8(9);
        image.Frames[1].Dilate(StructuringElement.CreateEllipse(1, 1), cancellationToken: Ct);
        Assert.Equal(0, image.Frames[0][3, 1].Value);
        Assert.Equal(9, image.Frames[0][2, 1].Value);
        Assert.Equal(9, image.Frames[1][2, 1].Value);
        Assert.Equal(9, image.Frames[1][3, 0].Value);
        Assert.Equal(0, image.Frames[1][2, 0].Value);

        image.Erode(StructuringElement.CreateRectangle(0, 0), cancellationToken: Ct);
        image.TopHat(StructuringElement.CreateEllipse(2, 1), cancellationToken: Ct);
        image.PercentileFilter(0, 10, cancellationToken: Ct);
        Assert.Equal(0, image.Owner.Scope.GetLiveBytes(Internals.AllocationKind.Temporary));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);

        using var source = new CancellationTokenSource();
        source.Cancel();
        using var canceled = CreateNoise();
        var before = ReadGray8(canceled);
        Assert.Throws<OperationCanceledException>(() => canceled.Erode(StructuringElement.CreateRectangle(1, 1), cancellationToken: source.Token));
        Assert.Throws<OperationCanceledException>(() => canceled.Frames[0].TopHat(StructuringElement.CreateRectangle(1, 1), cancellationToken: source.Token));
        Assert.Throws<OperationCanceledException>(() => canceled.PercentileFilter(1, 10, cancellationToken: source.Token));
        Assert.Equal(before, ReadGray8(canceled));
    }

    [Fact]
    public void Morphology_InvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>("radiusX", () => StructuringElement.CreateRectangle(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>("radiusY", () => StructuringElement.CreateEllipse(1, 1001));
        using var image = new Image<Rgb24>(2, 2);
        var element = StructuringElement.CreateRectangle(1, 1);
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).Erode(element));
        Assert.Throws<ArgumentNullException>("frame", () => ((ImageFrame)null!).Dilate(element));
        Assert.Throws<ArgumentNullException>("element", () => image.MorphologicalOpen(null!));
        Assert.Throws<ArgumentOutOfRangeException>("edgeMode", () => image.MorphologicalClose(element, (ConvolutionEdgeMode)9));
        Assert.Throws<ArgumentOutOfRangeException>("percentile", () => image.PercentileFilter(1, 100.5));
        Assert.Throws<ArgumentOutOfRangeException>("percentile", () => image.PercentileFilter(1, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>("radius", () => image.PercentileFilter(1001, 50));
    }

    /// <summary>The brute-force definition: the extremum of the samples of the element, read through the edge rule.</summary>
    private static byte Reference(byte[] samples, int width, int height, int channels, int channel, int x, int y, StructuringElement element, ConvolutionEdgeMode mode, bool dilate)
    {
        var result = dilate ? 0 : 255;
        for (var dy = -element.RadiusY; dy <= element.RadiusY; dy++)
        {
            for (var dx = -element.RadiusX; dx <= element.RadiusX; dx++)
            {
                if (!element.Contains(dx, dy))
                    continue;

                var sx = Map(x + dx, width, mode);
                var sy = Map(y + dy, height, mode);
                var value = sx < 0 || sy < 0 ? 0 : samples[(((sy * width) + sx) * channels) + channel];
                result = dilate ? Math.Max(result, value) : Math.Min(result, value);
            }
        }

        return (byte)result;

        static int Map(int index, int length, ConvolutionEdgeMode mode)
        {
            if (index >= 0 && index < length)
                return index;

            switch (mode)
            {
                case ConvolutionEdgeMode.Clamp:
                    return index < 0 ? 0 : length - 1;
                case ConvolutionEdgeMode.Wrap:
                    return ((index % length) + length) % length;
                case ConvolutionEdgeMode.Mirror:
                    var period = 2 * length;
                    var position = ((index % period) + period) % period;
                    return position < length ? position : period - 1 - position;
                default:
                    return -1;
            }
        }
    }

    /// <summary>24 x 17 gray samples of the linear congruential generator x = 1103515245 x + 12345 mod 2^31, from x = 12345: the bits 16 to 23 of each value.</summary>
    private static Image<Gray8> CreateNoise()
    {
        var image = new Image<Gray8>(Width, Height);
        long state = 12345;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                state = ((1103515245 * state) + 12345) % (1L << 31);
                image.Frames[0][x, y] = new Gray8((byte)((state >> 16) & 255));
            }
        }

        return image;
    }

    private static byte[] ReadGray8(Image<Gray8> image)
    {
        var bytes = new byte[image.Width * image.Height];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                bytes[(y * image.Width) + x] = image.Frames[0][x, y].Value;
            }
        }

        return bytes;
    }
}
