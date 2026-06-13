namespace Meziantou.Framework.LLMContext.Tests;

public sealed class LLMContextDetectorTests
{
    public static TheoryData<LLMContextKind, string, string> DetectionRules()
    {
        return new()
        {
            { LLMContextKind.ClaudeCode, "CLAUDECODE", "1" },
            { LLMContextKind.ClaudeCode, "CLAUDE_CODE_ENTRYPOINT", "1" },
            { LLMContextKind.Cursor, "CURSOR_EDITOR", "1" },
            { LLMContextKind.Cursor, "CURSOR_AI", "1" },
            { LLMContextKind.Gemini, "GEMINI_CLI", "true" },
            { LLMContextKind.GitHubCopilot, "GITHUB_COPILOT_CLI_MODE", "true" },
            { LLMContextKind.GitHubCopilot, "GH_COPILOT_WORKING_DIRECTORY", "1" },
            { LLMContextKind.GitHubCopilot, "COPILOT_CLI", "1" },
            { LLMContextKind.GitHubCopilot, "COPILOT_AGENT", "1" },
            { LLMContextKind.Codex, "CODEX_CLI", "1" },
            { LLMContextKind.Codex, "CODEX_SANDBOX", "1" },
            { LLMContextKind.Aider, "OR_APP_NAME", "Aider" },
            { LLMContextKind.Plandex, "OR_APP_NAME", "plandex" },
            { LLMContextKind.Amp, "AMP_HOME", "1" },
            { LLMContextKind.QwenCode, "QWEN_CODE", "1" },
            { LLMContextKind.Droid, "DROID_CLI", "true" },
            { LLMContextKind.OpenCode, "OPENCODE_AI", "1" },
            { LLMContextKind.ZedAI, "ZED_ENVIRONMENT", "1" },
            { LLMContextKind.ZedAI, "ZED_TERM", "1" },
            { LLMContextKind.KimiCLI, "KIMI_CLI", "true" },
            { LLMContextKind.OpenHands, "OR_APP_NAME", "OpenHands" },
            { LLMContextKind.Goose, "GOOSE_TERMINAL", "1" },
            { LLMContextKind.Cline, "CLINE_TASK_ID", "1" },
            { LLMContextKind.RooCode, "ROO_CODE_TASK_ID", "1" },
            { LLMContextKind.Windsurf, "WINDSURF_SESSION", "1" },
            { LLMContextKind.GenericAgent, "AGENT_CLI", "true" },
        };
    }

    [Theory]
    [MemberData(nameof(DetectionRules))]
    public void Detect_DetectsEachRule(LLMContextKind expected, string variable, string value)
    {
        var result = Detect((variable, value));

        Assert.Equal([expected], result);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("YES")]
    [InlineData("on")]
    [InlineData("ON")]
    public void Detect_AcceptsBooleanTrueValues(string value)
    {
        var result = Detect(("GEMINI_CLI", value));

        Assert.Equal([LLMContextKind.Gemini], result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("off")]
    [InlineData("invalid")]
    public void Detect_RejectsNonTrueBooleanValues(string? value)
    {
        var result = Detect(("GEMINI_CLI", value));

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Detect_RejectsMissingOrEmptyPresenceValues(string? value)
    {
        var result = Detect(("CLAUDECODE", value));

        Assert.Empty(result);
    }

    [Fact]
    public void Detect_MatchesExpectedValuesCaseInsensitively()
    {
        var result = Detect(("OR_APP_NAME", "aIdEr"));

        Assert.Equal([LLMContextKind.Aider], result);
    }

    [Fact]
    public void Detect_RejectsUnexpectedValue()
    {
        var result = Detect(("OR_APP_NAME", "other"));

        Assert.Empty(result);
    }

    [Fact]
    public void Detect_ReturnsAllMatchesInStableOrder()
    {
        var result = Detect(
            ("AGENT_CLI", "true"),
            ("CODEX_SANDBOX", "seatbelt"),
            ("CLAUDECODE", "1"),
            ("OPENCODE_AI", "1"));

        Assert.Equal(
            [
                LLMContextKind.ClaudeCode,
                LLMContextKind.Codex,
                LLMContextKind.OpenCode,
                LLMContextKind.GenericAgent,
            ],
            result);
    }

    [Fact]
    public void Detect_ReturnsEmptyWhenNoContextMatches()
    {
        Assert.Empty(LLMContextDetector.Detect(_ => null));
    }

    [Fact]
    public void IsLLMContext_ReturnsWhetherAContextMatches()
    {
        Assert.False(LLMContextDetector.IsLLMContext(_ => null));
        Assert.True(LLMContextDetector.IsLLMContext(name => name == "CODEX_CLI" ? "1" : null));
    }

    private static IReadOnlyList<LLMContextKind> Detect(params (string Name, string? Value)[] variables)
    {
        return LLMContextDetector.Detect(name => variables.FirstOrDefault(variable => variable.Name == name).Value);
    }
}
