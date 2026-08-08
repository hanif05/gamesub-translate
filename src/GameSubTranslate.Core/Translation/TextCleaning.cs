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