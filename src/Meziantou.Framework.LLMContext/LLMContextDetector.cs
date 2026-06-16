namespace Meziantou.Framework.LLMContext;

/// <summary>Detects known LLM and agentic execution contexts from environment variables.</summary>
public static class LLMContextDetector
{
    /// <summary>Detects all known LLM and agentic execution contexts in the current environment.</summary>
    /// <returns>The detected contexts, in a stable order.</returns>
    public static IReadOnlyList<LLMContextKind> Detect()
    {
        return Detect(Environment.GetEnvironmentVariable);
    }

    /// <summary>Determines whether the current environment is a known LLM or agentic execution context.</summary>
    /// <returns><see langword="true"/> when a known context is detected; otherwise, <see langword="false"/>.</returns>
    public static bool IsLLMContext()
    {
        return IsLLMContext(Environment.GetEnvironmentVariable);
    }

    internal static bool IsLLMContext(Func<string, string?> getEnvironmentVariable) => Detect(getEnvironmentVariable).Count > 0;

    internal static IReadOnlyList<LLMContextKind> Detect(Func<string, string?> getEnvironmentVariable)
    {
        var result = new List<LLMContextKind>();

        AddIf(LLMContextKind.ClaudeCode, IsAnyPresent("CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT"));
        AddIf(LLMContextKind.Cursor, IsAnyPresent("CURSOR_EDITOR", "CURSOR_AI"));
        AddIf(LLMContextKind.Gemini, IsBoolean("GEMINI_CLI"));
        AddIf(LLMContextKind.GitHubCopilot, IsBoolean("GITHUB_COPILOT_CLI_MODE") || IsAnyPresent("GH_COPILOT_WORKING_DIRECTORY", "COPILOT_CLI", "COPILOT_AGENT"));
        AddIf(LLMContextKind.Codex, IsAnyPresent("CODEX_CLI", "CODEX_SANDBOX"));
        AddIf(LLMContextKind.Aider, HasValue("OR_APP_NAME", "Aider"));
        AddIf(LLMContextKind.Plandex, HasValue("OR_APP_NAME", "plandex"));
        AddIf(LLMContextKind.Amp, IsAnyPresent("AMP_HOME"));
        AddIf(LLMContextKind.QwenCode, IsAnyPresent("QWEN_CODE"));
        AddIf(LLMContextKind.Droid, IsBoolean("DROID_CLI"));
        AddIf(LLMContextKind.OpenCode, IsAnyPresent("OPENCODE_AI"));
        AddIf(LLMContextKind.ZedAI, IsAnyPresent("ZED_ENVIRONMENT", "ZED_TERM"));
        AddIf(LLMContextKind.KimiCLI, IsBoolean("KIMI_CLI"));
        AddIf(LLMContextKind.OpenHands, HasValue("OR_APP_NAME", "OpenHands"));
        AddIf(LLMContextKind.Goose, IsAnyPresent("GOOSE_TERMINAL"));
        AddIf(LLMContextKind.Cline, IsAnyPresent("CLINE_TASK_ID"));
        AddIf(LLMContextKind.RooCode, IsAnyPresent("ROO_CODE_TASK_ID"));
        AddIf(LLMContextKind.Windsurf, IsAnyPresent("WINDSURF_SESSION"));
        AddIf(LLMContextKind.GenericAgent, IsBoolean("AGENT_CLI"));

        return result;

        void AddIf(LLMContextKind context, bool condition)
        {
            if (condition)
            {
                result.Add(context);
            }
        }

        bool IsAnyPresent(params ReadOnlySpan<string> variables)
        {
            foreach (var variable in variables)
            {
                if (!string.IsNullOrEmpty(getEnvironmentVariable(variable)))
                    return true;
            }

            return false;
        }

        bool IsBoolean(string variable)
        {
            return getEnvironmentVariable(variable) is { } value &&
                (value is "1" ||
                 value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("on", StringComparison.OrdinalIgnoreCase));
        }

        bool HasValue(string variable, string expectedValue)
        {
            return string.Equals(getEnvironmentVariable(variable), expectedValue, StringComparison.OrdinalIgnoreCase);
        }
    }
}
