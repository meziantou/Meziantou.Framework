using System.Diagnostics;

namespace Meziantou.Framework.Yaml;

internal static class CharacterAnalyzer
{
    /// <summary>
    /// Check if the character at the specified position is an alphabetical
    /// character, a digit, '_', or '-'.
    /// </summary>
    public static bool IsAlpha(ILookAheadBuffer buffer, int offset)
    {
        char character = buffer.Peek(offset);
        return
            (character >= '0' && character <= '9') ||
            (character >= 'A' && character <= 'Z') ||
            (character >= 'a' && character <= 'z') ||
            character == '_' ||
            character == '-';
    }

    public static bool IsAlpha(this ILookAheadBuffer buffer)
    {
        return IsAlpha(buffer, 0);
    }

    /// <summary>Check if the character is ASCII.</summary>
    public static bool IsAscii(this ILookAheadBuffer buffer, int offset)
    {
        return buffer.Peek(offset) <= '\x7F';
    }

    public static bool IsAscii(this ILookAheadBuffer buffer)
    {
        return IsAscii(buffer, 0);
    }

    public static bool IsPrintable(this ILookAheadBuffer buffer, int offset)
    {
        char character = buffer.Peek(offset);
        return Emitter.IsPrintable(character);
    }

    public static bool IsPrintable(this ILookAheadBuffer buffer)
    {
        return IsPrintable(buffer, 0);
    }

    /// <summary>Check if the character at the specified position is a digit.</summary>
    public static bool IsDigit(this ILookAheadBuffer buffer, int offset)
    {
        char character = buffer.Peek(offset);
        return character >= '0' && character <= '9';
    }

    public static bool IsDigit(this ILookAheadBuffer buffer)
    {
        return IsDigit(buffer, 0);
    }

    /// <summary>Get the value of a digit.</summary>
    public static int AsDigit(this ILookAheadBuffer buffer, int offset)
    {
        return buffer.Peek(offset) - '0';
    }

    public static int AsDigit(this ILookAheadBuffer buffer)
    {
        return AsDigit(buffer, 0);
    }

    /// <summary>Check if the character at the specified position is a hex-digit.</summary>
    public static bool IsHex(this ILookAheadBuffer buffer, int offset)
    {
        char character = buffer.Peek(offset);
        return
            (character >= '0' && character <= '9') ||
            (character >= 'A' && character <= 'F') ||
            (character >= 'a' && character <= 'f');
    }

    /// <summary>Get the value of a hex-digit.</summary>
    public static int AsHex(this ILookAheadBuffer buffer, int offset)
    {
        char character = buffer.Peek(offset);

        if (character <= '9')
        {
            return character - '0';
        }
        else if (character <= 'F')
        {
            return character - 'A' + 10;
        }
        else
        {
            return character - 'a' + 10;
        }
    }

    public static bool IsSpace(this ILookAheadBuffer buffer, int offset)
    {
        return Check(buffer, ' ', offset);
    }

    public static bool IsSpace(this ILookAheadBuffer buffer)
    {
        return IsSpace(buffer, 0);
    }

    /// <summary>Check if the character at the specified position is NUL.</summary>
    public static bool IsZero(this ILookAheadBuffer buffer, int offset)
    {
        return Check(buffer, '\0', offset);
    }

    public static bool IsZero(this ILookAheadBuffer buffer)
    {
        return IsZero(buffer, 0);
    }

    /// <summary>Check if the character at the specified position is tab.</summary>
    public static bool IsTab(this ILookAheadBuffer buffer, int offset)
    {
        return Check(buffer, '\t', offset);
    }

    public static bool IsTab(this ILookAheadBuffer buffer)
    {
        return IsTab(buffer, 0);
    }

    /// <summary>Check if the character at the specified position is blank (space or tab).</summary>
    public static bool IsBlank(this ILookAheadBuffer buffer, int offset)
    {
        return IsSpace(buffer, offset) || IsTab(buffer, offset);
    }

    public static bool IsBlank(this ILookAheadBuffer buffer)
    {
        return IsBlank(buffer, 0);
    }

    /// <summary>Check if the character at the specified position is a line break.</summary>
    public static bool IsBreak(this ILookAheadBuffer buffer, int offset)
    {
        return Check(buffer, "\r\n\x85\x2028\x2029", offset);
    }

    public static bool IsBreak(this ILookAheadBuffer buffer)
    {
        return IsBreak(buffer, 0);
    }

    public static bool IsCrLf(this ILookAheadBuffer buffer, int offset)
    {
        return Check(buffer, '\r', offset) && Check(buffer, '\n', offset + 1);
    }

    public static bool IsCrLf(this ILookAheadBuffer buffer)
    {
        return IsCrLf(buffer, 0);
    }

    /// <summary>Check if the character is a line break or NUL.</summary>
    public static bool IsBreakOrZero(this ILookAheadBuffer buffer, int offset)
    {
        return IsBreak(buffer, offset) || IsZero(buffer, offset);
    }

    public static bool IsBreakOrZero(this ILookAheadBuffer buffer)
    {
        return IsBreakOrZero(buffer, 0);
    }

    /// <summary>Check if the character is a line break, space, tab, or NUL.</summary>
    public static bool IsBlankOrBreakOrZero(this ILookAheadBuffer buffer, int offset)
    {
        return IsBlank(buffer, offset) || IsBreakOrZero(buffer, offset);
    }

    public static bool IsBlankOrBreakOrZero(this ILookAheadBuffer buffer)
    {
        return IsBlankOrBreakOrZero(buffer, 0);
    }

    public static bool Check(this ILookAheadBuffer buffer, char expected)
    {
        return Check(buffer, expected, 0);
    }

    public static bool Check(this ILookAheadBuffer buffer, char expected, int offset)
    {
        return buffer.Peek(offset) == expected;
    }

    public static bool Check(this ILookAheadBuffer buffer, string expectedCharacters)
    {
        return Check(buffer, expectedCharacters, 0);
    }

    public static bool Check(this ILookAheadBuffer buffer, string expectedCharacters, int offset)
    {
        Debug.Assert(expectedCharacters.Length > 1, "Use Check(char, int) instead.");

        char character = buffer.Peek(offset);

        foreach (var expected in expectedCharacters)
        {
            if (expected == character)
            {
                return true;
            }
        }
        return false;
    }
}