namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The predefined counter styles that are defined by @counter-style rules (https://drafts.csswg.org/css-counter-styles-3/#predefined-counters):
/// the numeric, alphabetic, symbolic and fixed styles of the specification. The longhand East Asian styles and
/// ethiopic-numeric have their own algorithm (see <see cref="CounterStyle"/>).
/// </summary>
/// <remarks>
/// The symbols are written with escapes. The symbols of disclosure-open and disclosure-closed depend on the writing mode and
/// on the direction: the ones of the style sheet are those of a horizontal left-to-right text.
/// </remarks>
internal static class PredefinedCounterStyles
{
    public const string StyleSheet = """
        @counter-style decimal {
            system: numeric;
            symbols: '0' '1' '2' '3' '4' '5' '6' '7' '8' '9';
        }
        @counter-style decimal-leading-zero {
            system: extends decimal;
            pad: 2 '0';
        }
        @counter-style arabic-indic {
            system: numeric;
            symbols: "\660" "\661" "\662" "\663" "\664" "\665" "\666" "\667" "\668" "\669";
        }
        @counter-style armenian {
            system: additive;
            range: 1 9999;
            additive-symbols:
                9000 \554, 8000 \553, 7000 \552, 6000 \551, 5000 \550, 4000 \54F, 3000 \54E, 2000 \54D, 1000 \54C, 900 \54B, 800 \54A,
                700 \549, 600 \548, 500 \547, 400 \546, 300 \545, 200 \544, 100 \543, 90 \542, 80 \541, 70 \540, 60 \53F, 50 \53E,
                40 \53D, 30 \53C, 20 \53B, 10 \53A, 9 \539, 8 \538, 7 \537, 6 \536, 5 \535, 4 \534, 3 \533, 2 \532, 1 \531;
        }
        @counter-style upper-armenian {
            system: extends armenian;
        }
        @counter-style lower-armenian {
            system: additive;
            range: 1 9999;
            additive-symbols:
                9000 "\584", 8000 "\583", 7000 "\582", 6000 "\581", 5000 "\580", 4000 "\57F", 3000 "\57E", 2000 "\57D", 1000 "\57C",
                900 "\57B", 800 "\57A", 700 "\579", 600 "\578", 500 "\577", 400 "\576", 300 "\575", 200 "\574", 100 "\573", 90 "\572",
                80 "\571", 70 "\570", 60 "\56F", 50 "\56E", 40 "\56D", 30 "\56C", 20 "\56B", 10 "\56A", 9 "\569", 8 "\568", 7 "\567",
                6 "\566", 5 "\565", 4 "\564", 3 "\563", 2 "\562", 1 "\561";
        }
        @counter-style bengali {
            system: numeric;
            symbols: "\9E6" "\9E7" "\9E8" "\9E9" "\9EA" "\9EB" "\9EC" "\9ED" "\9EE" "\9EF";
        }
        @counter-style cambodian {
            system: numeric;
            symbols: "\17E0" "\17E1" "\17E2" "\17E3" "\17E4" "\17E5" "\17E6" "\17E7" "\17E8" "\17E9";
        }
        @counter-style khmer {
            system: extends cambodian;
        }
        @counter-style cjk-decimal {
            system: numeric;
            range: 0 infinite;
            symbols: \3007  \4E00  \4E8C  \4E09  \56DB  \4E94  \516D  \4E03  \516B  \4E5D;
            suffix: "\3001";
        }
        @counter-style devanagari {
            system: numeric;
            symbols: "\966" "\967" "\968" "\969" "\96A" "\96B" "\96C" "\96D" "\96E" "\96F";
        }
        @counter-style georgian {
            system: additive;
            range: 1 19999;
            additive-symbols:
                10000 \10F5, 9000 \10F0, 8000 \10EF, 7000 \10F4, 6000 \10EE, 5000 \10ED, 4000 \10EC, 3000 \10EB, 2000 \10EA, 1000 \10E9,
                900 \10E8, 800 \10E7, 700 \10E6, 600 \10E5, 500 \10E4, 400 \10F3, 300 \10E2, 200 \10E1, 100 \10E0, 90 \10DF, 80 \10DE,
                70 \10DD, 60 \10F2, 50 \10DC, 40 \10DB, 30 \10DA, 20 \10D9, 10 \10D8, 9 \10D7, 8 \10F1, 7 \10D6, 6 \10D5, 5 \10D4,
                4 \10D3, 3 \10D2, 2 \10D1, 1 \10D0;
        }
        @counter-style gujarati {
            system: numeric;
            symbols: "\AE6" "\AE7" "\AE8" "\AE9" "\AEA" "\AEB" "\AEC" "\AED" "\AEE" "\AEF";
        }
        @counter-style gurmukhi {
            system: numeric;
            symbols: "\A66" "\A67" "\A68" "\A69" "\A6A" "\A6B" "\A6C" "\A6D" "\A6E" "\A6F";
        }
        @counter-style hebrew {
            system: additive;
            range: 1 10999;
            additive-symbols:
                10000 \5D9\5F3, 9000 \5D8\5F3, 8000 \5D7\5F3, 7000 \5D6\5F3, 6000 \5D5\5F3, 5000 \5D4\5F3, 4000 \5D3\5F3, 3000 \5D2\5F3,
                2000 \5D1\5F3, 1000 \5D0\5F3, 400 \5EA, 300 \5E9, 200 \5E8, 100 \5E7, 90 \5E6, 80 \5E4, 70 \5E2, 60 \5E1, 50 \5E0,
                40 \5DE, 30 \5DC, 20 \5DB, 19 \5D9\5D8, 18 \5D9\5D7, 17 \5D9\5D6, 16 \5D8\5D6, 15 \5D8\5D5, 10 \5D9, 9 \5D8, 8 \5D7,
                7 \5D6, 6 \5D5, 5 \5D4, 4 \5D3, 3 \5D2, 2 \5D1, 1 \5D0;
        }
        @counter-style kannada {
            system: numeric;
            symbols: "\CE6" "\CE7" "\CE8" "\CE9" "\CEA" "\CEB" "\CEC" "\CED" "\CEE" "\CEF";
        }
        @counter-style lao {
            system: numeric;
            symbols: "\ED0" "\ED1" "\ED2" "\ED3" "\ED4" "\ED5" "\ED6" "\ED7" "\ED8" "\ED9";
        }
        @counter-style malayalam {
            system: numeric;
            symbols: "\D66" "\D67" "\D68" "\D69" "\D6A" "\D6B" "\D6C" "\D6D" "\D6E" "\D6F";
        }
        @counter-style mongolian {
            system: numeric;
            symbols: "\1810" "\1811" "\1812" "\1813" "\1814" "\1815" "\1816" "\1817" "\1818" "\1819";
        }
        @counter-style myanmar {
            system: numeric;
            symbols: "\1040" "\1041" "\1042" "\1043" "\1044" "\1045" "\1046" "\1047" "\1048" "\1049";
        }
        @counter-style oriya {
            system: numeric;
            symbols: "\B66" "\B67" "\B68" "\B69" "\B6A" "\B6B" "\B6C" "\B6D" "\B6E" "\B6F";
        }
        @counter-style persian {
            system: numeric;
            symbols: "\6F0" "\6F1" "\6F2" "\6F3" "\6F4" "\6F5" "\6F6" "\6F7" "\6F8" "\6F9";
        }
        @counter-style lower-roman {
            system: additive;
            range: 1 3999;
            additive-symbols: 1000 m, 900 cm, 500 d, 400 cd, 100 c, 90 xc, 50 l, 40 xl, 10 x, 9 ix, 5 v, 4 iv, 1 i;
        }
        @counter-style upper-roman {
            system: additive;
            range: 1 3999;
            additive-symbols: 1000 M, 900 CM, 500 D, 400 CD, 100 C, 90 XC, 50 L, 40 XL, 10 X, 9 IX, 5 V, 4 IV, 1 I;
        }
        @counter-style tamil {
            system: numeric;
            symbols: "\BE6" "\BE7" "\BE8" "\BE9" "\BEA" "\BEB" "\BEC" "\BED" "\BEE" "\BEF";
        }
        @counter-style telugu {
            system: numeric;
            symbols: "\C66" "\C67" "\C68" "\C69" "\C6A" "\C6B" "\C6C" "\C6D" "\C6E" "\C6F";
        }
        @counter-style thai {
            system: numeric;
            symbols: "\E50" "\E51" "\E52" "\E53" "\E54" "\E55" "\E56" "\E57" "\E58" "\E59";
        }
        @counter-style tibetan {
            system: numeric;
            symbols: "\F20" "\F21" "\F22" "\F23" "\F24" "\F25" "\F26" "\F27" "\F28" "\F29";
        }
        @counter-style lower-alpha {
            system: alphabetic;
            symbols: a b c d e f g h i j k l m n o p q r s t u v w x y z;
        }
        @counter-style lower-latin {
            system: extends lower-alpha;
        }
        @counter-style upper-alpha {
            system: alphabetic;
            symbols: A B C D E F G H I J K L M N O P Q R S T U V W X Y Z;
        }
        @counter-style upper-latin {
            system: extends upper-alpha;
        }
        @counter-style lower-greek {
            system: alphabetic;
            symbols:
                "\3B1" "\3B2" "\3B3" "\3B4" "\3B5" "\3B6" "\3B7" "\3B8" "\3B9" "\3BA" "\3BB" "\3BC" "\3BD" "\3BE" "\3BF" "\3C0" "\3C1"
                "\3C3" "\3C4" "\3C5" "\3C6" "\3C7" "\3C8" "\3C9";
        }
        @counter-style hiragana {
            system: alphabetic;
            symbols:
                "\3042" "\3044" "\3046" "\3048" "\304A" "\304B" "\304D" "\304F" "\3051" "\3053" "\3055" "\3057" "\3059" "\305B" "\305D"
                "\305F" "\3061" "\3064" "\3066" "\3068" "\306A" "\306B" "\306C" "\306D" "\306E" "\306F" "\3072" "\3075" "\3078" "\307B"
                "\307E" "\307F" "\3080" "\3081" "\3082" "\3084" "\3086" "\3088" "\3089" "\308A" "\308B" "\308C" "\308D" "\308F" "\3090"
                "\3091" "\3092" "\3093";
            suffix: "\003001";
        }
        @counter-style hiragana-iroha {
            system: alphabetic;
            symbols:
                "\3044" "\308D" "\306F" "\306B" "\307B" "\3078" "\3068" "\3061" "\308A" "\306C" "\308B" "\3092" "\308F" "\304B" "\3088"
                "\305F" "\308C" "\305D" "\3064" "\306D" "\306A" "\3089" "\3080" "\3046" "\3090" "\306E" "\304A" "\304F" "\3084" "\307E"
                "\3051" "\3075" "\3053" "\3048" "\3066" "\3042" "\3055" "\304D" "\3086" "\3081" "\307F" "\3057" "\3091" "\3072" "\3082"
                "\305B" "\3059";
            suffix: "\003001";
        }
        @counter-style katakana {
            system: alphabetic;
            symbols:
                "\30A2" "\30A4" "\30A6" "\30A8" "\30AA" "\30AB" "\30AD" "\30AF" "\30B1" "\30B3" "\30B5" "\30B7" "\30B9" "\30BB" "\30BD"
                "\30BF" "\30C1" "\30C4" "\30C6" "\30C8" "\30CA" "\30CB" "\30CC" "\30CD" "\30CE" "\30CF" "\30D2" "\30D5" "\30D8" "\30DB"
                "\30DE" "\30DF" "\30E0" "\30E1" "\30E2" "\30E4" "\30E6" "\30E8" "\30E9" "\30EA" "\30EB" "\30EC" "\30ED" "\30EF" "\30F0"
                "\30F1" "\30F2" "\30F3";
            suffix: "\003001";
        }
        @counter-style katakana-iroha {
            system: alphabetic;
            symbols:
                "\30A4" "\30ED" "\30CF" "\30CB" "\30DB" "\30D8" "\30C8" "\30C1" "\30EA" "\30CC" "\30EB" "\30F2" "\30EF" "\30AB" "\30E8"
                "\30BF" "\30EC" "\30BD" "\30C4" "\30CD" "\30CA" "\30E9" "\30E0" "\30A6" "\30F0" "\30CE" "\30AA" "\30AF" "\30E4" "\30DE"
                "\30B1" "\30D5" "\30B3" "\30A8" "\30C6" "\30A2" "\30B5" "\30AD" "\30E6" "\30E1" "\30DF" "\30B7" "\30F1" "\30D2" "\30E2"
                "\30BB" "\30B9";
            suffix: "\003001";
        }
        @counter-style disc {
            system: cyclic;
            symbols: \2022;
            suffix: " ";
        }
        @counter-style circle {
            system: cyclic;
            symbols: \25E6;
            suffix: " ";
        }
        @counter-style square {
            system: cyclic;
            symbols: \25AA;
            suffix: " ";
        }
        @counter-style disclosure-open {
            system: cyclic;
            symbols: "\25BE";
            suffix: " ";
        }
        @counter-style disclosure-closed {
            system: cyclic;
            symbols: "\25B8";
            suffix: " ";
        }
        @counter-style cjk-earthly-branch {
            system: fixed;
            symbols: "\5B50" "\4E11" "\5BC5" "\536F" "\8FB0" "\5DF3" "\5348" "\672A" "\7533" "\9149" "\620C" "\4EA5";
            suffix: "\003001";
            fallback: cjk-decimal;
        }
        @counter-style cjk-heavenly-stem {
            system: fixed;
            symbols: "\7532" "\4E59" "\4E19" "\4E01" "\620A" "\5DF1" "\5E9A" "\8F9B" "\58EC" "\7678";
            suffix: "\003001";
            fallback: cjk-decimal;
        }
        """;
}
