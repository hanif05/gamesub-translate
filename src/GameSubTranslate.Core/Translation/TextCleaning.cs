using System.Text.RegularExpressions;

namespace GameSubTranslate.Translation;

/// <summary>
/// Shared text cleanup used by both <see cref="TranslationClient"/> and
/// <see cref="Ocr.VisionAiOcrEngine"/>. Reasoning models (qwen3, deepseek-r1, o1, ...)
/// prepend a <think>...</think> block to <c>message.content</c> before the answer — that
/// block can be tens of KB and is never what the caller wants. We strip the most common
/// variants rather than model-detection (which would couple us to specific model lists).
/// </summary>
internal static class TextCleaning
{
    /// <summary>Strips reasoning chain-of-thought blocks from a model response. Returns the
    /// trimmed remainder; never returns null.</summary>
    public static string StripThinking(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        // <think>...</think> (qwen3, deepseek-r1).
        var s = StripBetween(raw, "<think>", "</think>");

        // <reasoning>...</reasoning> (some Anthropic-style responses).
        s = StripBetween(s, "<reasoning>", "</reasoning>");

        // <thought>...</thought> (rare, but cheap to handle).
        s = StripBetween(s, "<thought>", "</thought>");

        return s.Trim();
    }

    /// <summary>
    /// Normalizes raw OCR text into a stable cache key / change-detection form. OCR noise varies
    /// frame-to-frame (box-drawing glyphs, character-nameplate fragments, leading/trailing
    /// punctuation), which otherwise makes the same dialog line read as a *different* string every
    /// cycle and re-triggers translation. Returns "" when nothing useful survives (pure decoration
    /// or garbage), so the pipeline treats the cycle as "no text".
    /// </summary>
    public static string NormalizeForCache(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var lines = raw.Split('\n').Select(l => l.Trim()).ToList();
        // Layer 1 (Fix 4): drop pure decoration. A line survives only if it's mostly
        // letters *and* carries a real word — this kills nameplate fragments ("SS Bu as",
        // "Hugh De") and box-drawing glyphs ("\.. 1 =", "fl A") while keeping short dialog.
        lines = lines.Where(l => l.Length > 0
            && CountLetters(l) >= 0.4 * l.Length
            && !(MaxWordLen(l) < 4 && CountLetters(l) < 8)).ToList();
        if (lines.Count == 0) return "";

        // Layer 2: keep only the dominant line. The dialog line dwarfs any nameplate fragment
        // that slipped past layer 1 ("~ Hugh ss & Scere" vs "Diana, aren't you tired?") — keep
        // the longest, since a subtitle box holds one active dialog line.
        int maxLetters = lines.Max(CountLetters);
        lines = lines.Where(l => CountLetters(l) == maxLetters).ToList();
        if (lines.Count == 0) return "";

        // Layer 3 (Fix 4): a single leftover line that is short, has no real long word, and
        // carries no sentence punctuation is a nameplate fragment ("Hugh De", "fl A") — drop it.
        // Real dialog ("Diana, aren't you tired?") always clears these bars.
        if (lines.Count == 1 && lines[0].Count(char.IsLetter) < 10
            && MaxWordLen(lines[0]) < 5
            && !lines[0].Any(c => c is '.' or '!' or '?' or '…'))
            return "";

        var joined = string.Join(' ', lines);
        joined = Regex.Replace(joined, @"\s+", " ");

        // Strip leading short-word noise before the first real word ("A A Diana, ..." → "Diana, ...")
        // and trailing fragments after sentence punctuation ("tired? g" → "tired?").
        joined = StripLeadingNoise(joined);
        joined = StripTrailingNoise(joined);
        return joined.Trim();
    }

    /// <summary>Drops a leading run of short-word noise: everything before the first word with
    /// ≥ 4 letters. "A A Diana" → "Diana". No word qualifies → unchanged.</summary>
    private static string StripLeadingNoise(string s)
    {
        var tokens = s.Split(' ');
        for (int k = 0; k < tokens.Length; k++)
            if (tokens[k].Count(char.IsLetter) >= 4)
                return string.Join(' ', tokens.Skip(k));
        return s;
    }

    /// <summary>Drops a short fragment after the final sentence punctuation ("tired? g" →
    /// "tired?"), but keeps a real continuation ("I'm fine. Thanks").</summary>
    private static string StripTrailingNoise(string s)
    {
        int cut = -1;
        for (int i = s.Length - 1; i >= 0; i--)
            if (s[i] is '.' or '!' or '?' or '…') { cut = i + 1; break; }
        if (cut < 0 || cut >= s.Length) return s;

        var tail = s[cut..].Trim();
        if (tail.Length == 0) return s.TrimEnd();
        var tailWords = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool allShort = tailWords.All(w => w.Count(char.IsLetter) < 4);
        return allShort ? s[..cut].TrimEnd() : s;
    }

    /// <summary>Longest word (by letters+digits) in a line — distinguishes real words from
    /// 1-2 char glyph fragments.</summary>
    private static int MaxWordLen(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
         .DefaultIfEmpty("")
         .Max(w => w.Count(char.IsLetterOrDigit));

    /// <summary>Counts letters+digits in a line (what OCR should preserve); everything else is
    /// decoration to be ignored for the keep/drop decision.</summary>
    private static int CountLetters(string s) => s.Count(char.IsLetterOrDigit);

    private static string StripBetween(string s, string openTag, string closeTag)
    {
        var start = s.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return s;

        var end = s.IndexOf(closeTag, start + openTag.Length, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            // Unclosed tag — the model emitted thinking but never closed it (often a sign the
            // generation was truncated). Drop everything from the opening tag onward; we'd
            // rather show nothing than leak half a reasoning chain to the overlay.
            return s[..start];
        }
        // Drop the open tag, the block, and the close tag. If anything (e.g. a stray newline)
        // sits between the close tag and the real answer, collapse the resulting whitespace.
        var after = end + closeTag.Length;
        var rest = (s[..start] + s[after..]).TrimStart();
        return rest;
    }
}