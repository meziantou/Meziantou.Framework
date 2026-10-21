# Meziantou.Framework.CommandLine

A .NET library that provides utilities for working with command-line arguments, including parsing, building/escaping arguments, and creating interactive console prompts.

## Features

- **CommandLineParser**: Parse command-line arguments with support for named and positional arguments
- **CommandLineBuilder**: Properly escape and quote arguments for Windows command-line and cmd.exe
- **Prompt**: Create interactive yes/no prompts in console applications

## Usage

The types are in the `Meziantou.Framework` namespace.

### CommandLineParser

Parse command-line arguments with support for named arguments (with `/` or `-` prefix) and positional arguments.

<!-- ref:../../tests/Meziantou.Framework.CommandLineTests/CommandLineParserTests.cs;region=CommandLineParser_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Parse current process arguments
var currentParser = CommandLineParser.Current;
Assert.NotNull(currentParser);

// Or parse custom arguments
var parser = new CommandLineParser();
parser.Parse(["/name=John", "/verbose", "input.txt"]);

// Check if an argument exists
Assert.True(parser.HasArgument("verbose"));

// Get named argument value
Assert.Equal("John", parser.GetArgument("name"));

// Get positional argument (the index in the array of arguments)
Assert.Equal("input.txt", parser.GetArgument(2));

// Check if help was requested: detects -?, /?, -help, /help, --help
Assert.False(parser.HelpRequested);
```
<!-- endref -->

**Supported argument formats:**
- Named with equals: `/name=value` or `-name=value`
- Named with colon: `/name:value` or `-name:value`
- Named without value: `/verbose` or `-verbose`
- Positional: any argument without prefix

### CommandLineBuilder

Properly escape and quote arguments for safe command-line execution on Windows.

<!-- ref:../../tests/Meziantou.Framework.CommandLineTests/CommandLineBuilderTests.cs;region=CommandLineBuilder_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Quote a single argument for standard Windows applications
var arg = CommandLineBuilder.WindowsQuotedArgument(@"path with spaces\file.txt");
Assert.Equal(@"""path with spaces\file.txt""", arg);

// Quote multiple arguments
var args = CommandLineBuilder.WindowsQuotedArguments("arg1", "path with spaces", "normal");
Assert.Equal(@"arg1 ""path with spaces"" normal", args);

// Quote for cmd.exe (handles special characters like &, |, ^, etc.)
var cmdArg = CommandLineBuilder.WindowsCmdArgument(@"malicious argument"" & whoami");
Assert.Equal(@"^""malicious argument\^"" ^& whoami^""", cmdArg);

var cmdArgs = CommandLineBuilder.WindowsCmdArguments("echo", "Hello & Goodbye");
Assert.Equal(@"echo ^""Hello ^& Goodbye^""", cmdArgs);
```
<!-- endref -->

**Why use CommandLineBuilder?**
- Handles spaces, quotes, and backslashes correctly
- Prevents command injection attacks
- `WindowsCmdArgument` handles cmd.exe special characters: `(`, `)`, `%`, `!`, `^`, `"`, `<`, `>`, `&`, `|`
- Based on Microsoft's article: [Everyone quotes command line arguments the wrong way](https://blogs.msdn.microsoft.com/twistylittlepassagesallalike/2011/04/23/everyone-quotes-command-line-arguments-the-wrong-way/)

### Prompt

Create interactive yes/no prompts in console applications.

<!-- ref:../../samples/Meziantou.Framework.CommandLine.Samples/ReadmeExamples.cs;region=Prompt_YesNo;format=md-fenced-code-block;dedent=true -->
```csharp
// Simple yes/no prompt with default value
var proceed = Prompt.YesNo("Do you want to continue?", defaultValue: true);
// Displays: Do you want to continue? [Y/n]
// User can press Enter to use default (true)

// Without default value
var confirm = Prompt.YesNo("Are you sure?", defaultValue: null);
// Displays: Are you sure? [y/n]
// User must enter y or n

// Custom yes/no labels
var delete = Prompt.YesNo("Delete file?", "Yes", "No", defaultValue: false);
// Displays: Delete file? [y/N]

// Prompts are case-insensitive and loop until valid input
var result = Prompt.YesNo("Enable feature?", defaultValue: true);
if (result)
{
    Console.WriteLine("Feature enabled!");
}
```
<!-- endref -->
