using System;
using System.Globalization;

namespace FossPDF.Drawing
{
    /// <summary>
    /// Implements the CJK subset of the Unicode line breaking rules (UAX #14) that matters for
    /// text without spaces: a line may break between most ideographic, kana and Hangul characters,
    /// but not before closing punctuation, small kana or iteration marks, and not after opening
    /// punctuation or currency prefixes. Latin words keep the space based wrapping rules.
    /// </summary>
    internal static class CjkLineBreak
    {
        // Prohibited at the start of a line: closing brackets, closing punctuation, small kana,
        // prolonged sound marks and iteration marks.
        private const string ProhibitedLineStartCharacters =
            ")]}»”’〙〗〟｠" +
            "〉》」』】〕〗〙〛＞｣" +
            "、。，．・：；？！｡､" +
            "ぁぃぅぇぉっゃゅょゎゕゖ" +
            "ァィゥェォッャュョヮヵヶ" +
            "ー〜～々〻ゝゞヽヾ";

        // Prohibited at the end of a line: opening brackets and quotes, and currency prefixes.
        private const string ProhibitedLineEndCharacters =
            "([{«“‘〘〖〝｟" +
            "〈《「『【〔〗〚＜｢" +
            "（［｛" +
            "¥￥＄$£€";

        public static bool CanBreakBetween(int before, int after)
        {
            if (!IsCjkCodePoint(before) && !IsCjkCodePoint(after))
                return false;

            // ZWJ and ZWNJ join characters (emoji sequences, Arabic, Indic) and must stay attached.
            if (before == 0x200C || before == 0x200D || after == 0x200C || after == 0x200D)
                return false;

            // Variation selectors and emoji modifiers belong to the preceding character.
            if (after == 0xFE0E || after == 0xFE0F)
                return false;

            if (after >= 0x1F3FB && after <= 0x1F3FF)
                return false;

            // Non-breaking space glues both of its neighbours.
            if (before == 0x00A0 || after == 0x00A0)
                return false;

            // Never separate a combining mark from its base character.
            if (IsCombiningMark(after))
                return false;

            if (IsProhibitedLineStart(after))
                return false;

            if (IsProhibitedLineEnd(before))
                return false;

            return true;
        }

        public static bool IsCjkCodePoint(int codePoint)
        {
            return
                (codePoint >= 0x1100 && codePoint <= 0x11FF) || // Hangul Jamo
                (codePoint >= 0x2E80 && codePoint <= 0x2EFF) || // CJK radicals supplement
                (codePoint >= 0x2F00 && codePoint <= 0x2FDF) || // Kangxi radicals
                (codePoint >= 0x2FF0 && codePoint <= 0x2FFF) || // ideographic description
                (codePoint >= 0x3000 && codePoint <= 0x303F) || // CJK symbols and punctuation
                (codePoint >= 0x3040 && codePoint <= 0x309F) || // Hiragana
                (codePoint >= 0x30A0 && codePoint <= 0x30FF) || // Katakana
                (codePoint >= 0x3100 && codePoint <= 0x312F) || // Bopomofo
                (codePoint >= 0x3130 && codePoint <= 0x318F) || // Hangul compatibility Jamo
                (codePoint >= 0x3190 && codePoint <= 0x319F) || // Kanbun
                (codePoint >= 0x31F0 && codePoint <= 0x31FF) || // Katakana phonetic extensions
                (codePoint >= 0x3200 && codePoint <= 0x33FF) || // enclosed/compatibility CJK
                (codePoint >= 0x3400 && codePoint <= 0x4DBF) || // CJK extension A
                (codePoint >= 0x4E00 && codePoint <= 0x9FFF) || // CJK unified ideographs
                (codePoint >= 0xA960 && codePoint <= 0xA97F) || // Hangul Jamo extension A
                (codePoint >= 0xAC00 && codePoint <= 0xD7AF) || // Hangul syllables
                (codePoint >= 0xD7B0 && codePoint <= 0xD7FF) || // Hangul Jamo extension B
                (codePoint >= 0xF900 && codePoint <= 0xFAFF) || // CJK compatibility ideographs
                (codePoint >= 0xFE10 && codePoint <= 0xFE1F) || // vertical forms
                (codePoint >= 0xFE30 && codePoint <= 0xFE4F) || // CJK compatibility forms
                (codePoint >= 0xFF00 && codePoint <= 0xFFEF) || // halfwidth and fullwidth forms
                (codePoint >= 0x1B000 && codePoint <= 0x1B16F) || // kana supplement and extension
                (codePoint >= 0x20000 && codePoint <= 0x2FA1F) || // CJK extension B and later
                (codePoint >= 0x30000 && codePoint <= 0x3134F); // CJK extension G
        }

        public static bool IsProhibitedLineStart(int codePoint)
        {
            return IndexOf(ProhibitedLineStartCharacters, codePoint) >= 0 ||
                   (codePoint >= 0x31F0 && codePoint <= 0x31FF); // small kana extension
        }

        public static bool IsProhibitedLineEnd(int codePoint)
        {
            return IndexOf(ProhibitedLineEndCharacters, codePoint) >= 0;
        }

        private static bool IsCombiningMark(int codePoint)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(codePoint))
            {
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                    return true;

                default:
                    return false;
            }
        }

        private static int IndexOf(string characters, int codePoint)
        {
            for (var i = 0; i < characters.Length; i++)
            {
                var current = char.ConvertToUtf32(characters, i);

                if (current == codePoint)
                    return i;

                if (char.IsSurrogatePair(characters, i))
                    i++;
            }

            return -1;
        }
    }
}
