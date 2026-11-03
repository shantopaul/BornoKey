using System.Globalization;
using System.Text;

namespace BengaliKeyboard.Input;

/// <summary>
/// Represents the result of composing or backspacing Bengali Unicode text,
/// including the exact number of target-application backspaces and new text to inject.
/// </summary>
public readonly record struct UnicodeCompositionResult(
    string UpdatedBuffer,
    int BackspaceCount,
    string TextToInject);

/// <summary>
/// Standards-compliant Bengali Unicode composition, vowel-sign (Kar) attachment,
/// Hasanta/Juktakhor cluster normalization, and grapheme-aware backspace diffing engine.
/// </summary>
public sealed class UnicodeProcessor
{
    private const char Hasanta = '\u09CD';       // ্
    private const char Nukta = '\u09BC';         // ়
    private const char AssameseRa = '\u09F0';
    private const char BengaliA = '\u0985';      // অ
    private const char BengaliAa = '\u0986';     // আ
    private const char AaKar = '\u09BE';         // া
    private const char IKar = '\u09BF';          // ি
    private const char IiKar = '\u09C0';         // ী
    private const char UKar = '\u09C1';          // ু
    private const char UuKar = '\u09C2';         // ূ
    private const char RriKar = '\u09C3';        // ৃ
    private const char EKar = '\u09C7';          // ে
    private const char AiKar = '\u09C8';         // ৈ
    private const char OKar = '\u09CB';          // ো
    private const char AuKar = '\u09CC';         // ৌ
    private const char AuLengthMark = '\u09D7';  // ৗ

    /// <summary>
    /// Appends raw Unicode input to the current composition buffer, applying Bengali orthographic rules
    /// (अ + া -> আ, ে + া -> ো, ে + ৗ -> ৌ, duplicate vowel-sign replacement, duplicate Hasanta prevention,
    /// Nukta precomposition, and NFC normalization) and computes the exact Backspace + Inject diff.
    /// </summary>
    public UnicodeCompositionResult Compose(string currentBuffer, string newInput)
    {
        currentBuffer ??= string.Empty;
        if (string.IsNullOrEmpty(newInput))
        {
            return new UnicodeCompositionResult(currentBuffer, 0, string.Empty);
        }

        var working = new StringBuilder(currentBuffer);

        foreach (var ch in newInput)
        {
            AppendCodePointWithRules(working, ch);
        }

        var normalizedNew = NormalizeBengaliText(working.ToString());
        return ComputeDiff(currentBuffer, normalizedNew);
    }

    /// <summary>
    /// Deletes the trailing composition unit (dependent vowel sign, diacritic, or trailing grapheme cluster)
    /// and computes the exact number of backspaces and replacement characters required in the target app.
    /// </summary>
    public UnicodeCompositionResult Backspace(string currentBuffer, bool deleteEntireGraphemeCluster = false)
    {
        if (string.IsNullOrEmpty(currentBuffer))
        {
            return new UnicodeCompositionResult(string.Empty, 1, string.Empty);
        }

        string updated;
        if (deleteEntireGraphemeCluster)
        {
            updated = RemoveLastGraphemeCluster(currentBuffer);
        }
        else
        {
            updated = RemoveLastCodePointOrModifier(currentBuffer);
        }

        return ComputeDiff(currentBuffer, updated);
    }

    /// <summary>
    /// Removes the last full Bengali orthographic grapheme cluster (Akshara), including multi-consonant
    /// Hasanta conjuncts (e.g., <c>ক্ষ</c>, <c>জ্ঞ</c>, <c>ত্র</c>, <c>স্ত্র</c>) and attached vowel signs.
    /// </summary>
    public static string RemoveLastGraphemeCluster(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var clusterIndices = ParseBengaliGraphemeClusterIndices(text);
        if (clusterIndices.Count <= 1)
        {
            return string.Empty;
        }

        var lastClusterStartIndex = clusterIndices[^1];
        return text[..lastClusterStartIndex];
    }

    /// <summary>
    /// Removes the trailing Unicode scalar value (or decomposing modifier such as a trailing Kar/Hasanta)
    /// so users can fix a mistyped Kar (<c>কা</c> -&gt; <c>ক</c>) with a single Backspace, or step back
    /// through a conjunct (<c>ক্ষ</c> -&gt; <c>ক্</c> -&gt; <c>ক</c>).
    /// </summary>
    public static string RemoveLastCodePointOrModifier(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Handle UTF-16 surrogate pairs (e.g., emojis) as a single scalar value
        if (text.Length >= 2 && char.IsLowSurrogate(text[^1]) && char.IsHighSurrogate(text[^2]))
        {
            return text[..^2];
        }

        return text[..^1];
    }

    /// <summary>
    /// Counts the number of user-perceived Bengali grapheme clusters (Aksharas) in the specified text,
    /// linking <c>Consonant + Hasanta + Consonant</c> sequences into single conjunct clusters.
    /// </summary>
    public static int CountGraphemeClusters(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return ParseBengaliGraphemeClusterIndices(text).Count;
    }

    /// <summary>
    /// Computes the start UTF-16 indices of each Bengali grapheme cluster (Akshara) in <paramref name="text"/>,
    /// extending <see cref="StringInfo.ParseCombiningCharacters(string)"/> with Indic Conjunct Linking (<c>্</c> + Consonant).
    /// </summary>
    public static IReadOnlyList<int> ParseBengaliGraphemeClusterIndices(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var baseIndices = StringInfo.ParseCombiningCharacters(text);
        if (baseIndices.Length <= 1)
        {
            return baseIndices;
        }

        var mergedIndices = new List<int>(baseIndices.Length) { baseIndices[0] };

        for (var i = 1; i < baseIndices.Length; i++)
        {
            var currentStart = baseIndices[i];
            var previousEndChar = text[currentStart - 1];
            var currentFirstChar = text[currentStart];

            // If the previous text element ended with Hasanta (্) and the current element begins with
            // a Bengali consonant (or Ya/Ra phala), link them into the same Bengali conjunct cluster.
            if (previousEndChar == Hasanta && (IsBengaliConsonant(currentFirstChar) || currentFirstChar == Hasanta))
            {
                continue;
            }

            mergedIndices.Add(currentStart);
        }

        return mergedIndices;
    }

    /// <summary>
    /// Counts the number of Unicode scalar values (<see cref="Rune"/>) in the specified text,
    /// which corresponds to the number of <c>VK_BACK</c> keystrokes needed in standard Win32/WPF editors.
    /// </summary>
    public static int CountUnicodeScalars(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        foreach (var _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Normalizes Bengali text to Unicode NFC while preserving standard Bengali precomposed Nukta consonants
    /// (`ড়`, `ঢ়`, `য়`) that Unicode NFC otherwise decomposes.
    /// </summary>
    public static string NormalizeBengaliText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // First normalize to FormC (composes ে + া -> ো and ে + ৗ -> ৌ)
        var nfc = input.Normalize(NormalizationForm.FormC);

        // Ensure অ + া is always normalized to আ
        if (nfc.Contains("\u0985\u09BE", StringComparison.Ordinal))
        {
            nfc = nfc.Replace("\u0985\u09BE", "\u0986", StringComparison.Ordinal);
        }

        return nfc;
    }

    /// <summary>
    /// Computes the minimal <c>(BackspaceCount, TextToInject)</c> transition from <paramref name="previousBuffer"/>
    /// to <paramref name="updatedBuffer"/> measured in Unicode scalar values (<see cref="Rune"/>).
    /// </summary>
    public static UnicodeCompositionResult ComputeDiff(string previousBuffer, string updatedBuffer)
    {
        previousBuffer ??= string.Empty;
        updatedBuffer ??= string.Empty;

        var oldRunes = previousBuffer.EnumerateRunes().ToArray();
        var newRunes = updatedBuffer.EnumerateRunes().ToArray();

        var commonPrefixRunes = 0;
        var minLength = Math.Min(oldRunes.Length, newRunes.Length);
        while (commonPrefixRunes < minLength && oldRunes[commonPrefixRunes] == newRunes[commonPrefixRunes])
        {
            commonPrefixRunes++;
        }

        var backspaceCount = oldRunes.Length - commonPrefixRunes;
        var textToInjectBuilder = new StringBuilder();
        for (var i = commonPrefixRunes; i < newRunes.Length; i++)
        {
            textToInjectBuilder.Append(newRunes[i].ToString());
        }

        return new UnicodeCompositionResult(updatedBuffer, backspaceCount, textToInjectBuilder.ToString());
    }

    public static bool IsBengaliConsonant(char c) =>
        (c >= '\u0995' && c <= '\u09B9')
        || c is '\u09CE' // ৎ (Khanda Ta)
            or '\u09DC'  // ড়
            or '\u09DD'  // ঢ়
            or '\u09DF'  // য়
            or AssameseRa;

    public static bool IsBengaliIndependentVowel(char c) =>
        (c >= '\u0985' && c <= '\u0994')
        || c is '\u09E0' or '\u09E1';

    public static bool IsBengaliVowelSign(char c) =>
        c is AaKar
            or IKar
            or IiKar
            or UKar
            or UuKar
            or RriKar
            or '\u09C4'
            or EKar
            or AiKar
            or OKar
            or AuKar
            or AuLengthMark
            or '\u09E2'
            or '\u09E3';

    public static bool IsBengaliDiacritic(char c) =>
        c is '\u0981' // ঁ Chandrabindu
            or '\u0982' // ং Anusvara
            or '\u0983' // ঃ Visarga
            or Nukta;

    public static bool IsWordBoundary(char c) =>
        char.IsWhiteSpace(c)
        || c is '।' or ',' or '.' or ';' or ':' or '!' or '?' or '-' or '—'
            or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '\'' or '/' or '\\';

    private static void AppendCodePointWithRules(StringBuilder buffer, char incoming)
    {
        if (buffer.Length == 0)
        {
            buffer.Append(incoming);
            return;
        }

        var last = buffer[^1];

        // Rule 1: অ (\u0985) + া (\u09BE) -> আ (\u0986)
        if (last == BengaliA && incoming == AaKar)
        {
            buffer[^1] = BengaliAa;
            return;
        }

        // Rule 2: Prevent duplicate consecutive Hasanta (্ + ্ -> single ্)
        if (last == Hasanta && incoming == Hasanta)
        {
            return;
        }

        // Rule 3: Prevent duplicate consecutive Nukta (় + ় -> single ়)
        if (last == Nukta && incoming == Nukta)
        {
            return;
        }

        // Rule 4: Composite & replacement Vowel Signs (Kars)
        if (IsBengaliVowelSign(last) && IsBengaliVowelSign(incoming))
        {
            // ে (\u09C7) + া (\u09BE) -> ো (\u09CB)
            if (last == EKar && incoming == AaKar)
            {
                buffer[^1] = OKar;
                return;
            }

            // ে (\u09C7) + ৗ (\u09D7) or ে + ৌ -> ৌ (\u09CC)
            if (last == EKar && incoming is AuLengthMark or AuKar or UKar)
            {
                buffer[^1] = AuKar;
                return;
            }

            // ি + ি -> ী (convenient double-tap upgrade)
            if (last == IKar && incoming == IKar)
            {
                buffer[^1] = IiKar;
                return;
            }

            // ু + ু -> ূ (convenient double-tap upgrade)
            if (last == UKar && incoming == UKar)
            {
                buffer[^1] = UuKar;
                return;
            }

            // Otherwise, replace the previous vowel sign cleanly instead of stacking invalid double Kars
            buffer[^1] = incoming;
            return;
        }

        // Rule 5: Vowel sign typed immediately after Hasanta on a consonant (e.g., ক্ + া -> কা)
        // Replaces the trailing Hasanta with the vowel sign unless it is independent composition
        if (last == Hasanta && IsBengaliVowelSign(incoming) && buffer.Length >= 2 && IsBengaliConsonant(buffer[^2]))
        {
            buffer[^1] = incoming;
            return;
        }

        buffer.Append(incoming);
    }
}
