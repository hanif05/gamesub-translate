using GameSubTranslate.Translation;
using Xunit;

namespace GameSubTranslate.Core.Tests.Translation;

/// <summary>Regression for reasoning models (qwen3, deepseek-r1) leaking <think> blocks into
/// the user-visible translation / OCR output.</summary>
public class TextCleaningTests
{
    [Fact]
    public void StripThinking_QwenStyle_KeepsAnswer()
    {
        var raw = "<think>The user wants this translated. Let me parse it carefully...</think>Halo, dunia!";
        Assert.Equal("Halo, dunia!", TextCleaning.StripThinking(raw));
    }

    [Fact]
    public void StripThinking_DeepSeekStyle_KeepsAnswer()
    {
        var raw = "<think>step 1: tokenize\nstep 2: ...</think>Bonjour le monde";
        Assert.Equal("Bonjour le monde", TextCleaning.StripThinking(raw));
    }

    [Fact]
    public void StripThinking_UnclosedBlock_DropsEverythingFromOpenTag()
    {
        // Truncated generation: close tag missing. Drop the whole tail rather than leak half a
        // reasoning chain onto the overlay.
        var raw = "Some preamble<think>reasoning that never finished";
        Assert.Equal("Some preamble", TextCleaning.StripThinking(raw));
    }

    [Fact]
    public void StripThinking_NoBlock_ReturnsTrimmed()
    {
        Assert.Equal("Halo", TextCleaning.StripThinking("  Halo  "));
        Assert.Equal("", TextCleaning.StripThinking(""));
        Assert.Equal("", TextCleaning.StripThinking(null));
    }

    [Fact]
    public void StripThinking_CaseInsensitive()
    {
        var raw = "<THINK>reasoning</THINK>result";
        Assert.Equal("result", TextCleaning.StripThinking(raw));
    }
}