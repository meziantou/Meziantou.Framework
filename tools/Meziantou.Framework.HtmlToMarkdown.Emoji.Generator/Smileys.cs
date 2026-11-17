#pragma warning disable MA0047 // Declare types in namespaces

/// <summary>
/// The smileys of the emoji extension of Meziantou.Framework.Markdown, by the name of their emoji in Unicode
/// (<c>emoji-test.txt</c>). The generator writes the shortcode of each emoji: a name that Unicode does not define, or an emoji
/// without a shortcode, fails the generation.
/// </summary>
internal static class Smileys
{
    public static IReadOnlyList<(string Smiley, string EmojiName)> All { get; } =
    [
        // Smiling
        (":)", "grinning face with big eyes"),
        (":-)", "grinning face with big eyes"),
        (":D", "grinning face with smiling eyes"),
        (":-D", "grinning face with smiling eyes"),
        ("x-)", "grinning squinting face"),
        ("X-)", "grinning squinting face"),
        (";)", "winking face"),
        (";-)", "winking face"),
        (":\")", "smiling face with smiling eyes"),
        (":-\")", "smiling face with smiling eyes"),
        ("8-)", "smiling face with sunglasses"),
        ("B-)", "smiling face with sunglasses"),

        // Halo and horns
        ("o:)", "smiling face with halo"),
        ("o:-)", "smiling face with halo"),
        ("O:)", "smiling face with halo"),
        ("O:-)", "smiling face with halo"),
        ("0:)", "smiling face with halo"),
        ("0:-)", "smiling face with halo"),
        ("]:)", "smiling face with horns"),
        ("]:-)", "smiling face with horns"),
        ("]:(", "angry face with horns"),
        ("]:-(", "angry face with horns"),

        // Tears
        (":')", "face with tears of joy"),
        (":'-)", "face with tears of joy"),
        (":,)", "face with tears of joy"),
        (":,-)", "face with tears of joy"),
        (":'D", "face with tears of joy"),
        (":'-D", "face with tears of joy"),
        (":,D", "face with tears of joy"),
        (":,-D", "face with tears of joy"),
        (":'(", "crying face"),
        (":'-(", "crying face"),
        (":,(", "crying face"),
        (":,-(", "crying face"),
        (":,'(", "loudly crying face"),
        (":,'-(", "loudly crying face"),
        (";(", "loudly crying face"),
        (";-(", "loudly crying face"),

        // Other faces
        (":(", "frowning face with open mouth"),
        (":-(", "frowning face with open mouth"),
        (":/", "confused face"),
        (":-/", "confused face"),
        (":|", "neutral face"),
        (":-|", "neutral face"),
        (":o", "face with open mouth"),
        (":-o", "face with open mouth"),
        (":O", "face with open mouth"),
        (":-O", "face with open mouth"),
        (":p", "face with tongue"),
        (":-p", "face with tongue"),
        (":P", "face with tongue"),
        (":-P", "face with tongue"),
        (":*", "kissing face"),
        (":-*", "kissing face"),
        (":s", "unamused face"),
        (":-s", "unamused face"),
        (":S", "unamused face"),
        (":-S", "unamused face"),
        (":z", "unamused face"),
        (":-z", "unamused face"),
        (":Z", "unamused face"),
        (":-Z", "unamused face"),
        (":$", "unamused face"),
        (":-$", "unamused face"),
        (">:(", "angry face"),
        (">:-(", "angry face"),
        (":@", "enraged face"),
        (":-@", "enraged face"),

        // Hearts
        ("<3", "red heart"),
        ("</3", "broken heart"),
        ("<\\3", "broken heart"),
    ];
}
