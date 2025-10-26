# Meziantou.Framework.CommandLine

A .NET library that provides utilities for working with command-line arguments, including parsing, building/escaping arguments, and creating interactive console prompts.

## Features

- **CommandLineParser**: Parse command-line arguments with support for named and positional arguments
- **CommandLineBuilder**: Properly escape and quote arguments for Windows command-line and cmd.exe
- **Prompt**: Create interactive yes/no prompts in console applications

## Usage

### CommandLineParser

Parse command-line arguments with support for named arguments (with `/` or `-` prefix) and positional arguments.

```csharp
using Meziantou.Framework;

// Parse current process arguments
var parser = CommandLineParser.Current;

// Or parse custom arguments
var customParser = new CommandLineParser();
customParser.Parse(new[] { "/name=John", "/verbose", "input.txt" });

// Check if an argument exists
if (parser.HasArgument("verbose"))
{
    Console.WriteLine("Verbose mode enabled");
}

// Get named argument value
var name = parser.GetArgument("name");
Console.WriteLine($"Name: {name}"); // Output: Name: John

// Get positional argument
var inputFile = parser.GetArgument(2); // Position-based argument
Console.WriteLine($"Input file: {inputFile}"); // Output: Input file: input.txt

// Check if help was requested
if (parser.HelpRequested) // Detects -?, /?, -help, /help, --help
{
    ShowHelp();
}
```

**Supported argument formats:**
- Named with equals: `/name=value` or `-name=value`
- Named with colon: `/name:value` or `-name:value`
- Named without value: `/verbose` or `-verbose`
- Positional: any argument without prefix

### CommandLineBuilder

Properly escape and quote arguments for safe command-line execution on Windows.

```csharp
using Meziantou.Framework;

// Quote a single argument for standard Windows applications
var arg = CommandLineBuilder.WindowsQuotedArgument(@"path with spaces\file.txt");
// Returns: "path with spaces\file.txt"

// Quote multiple arguments
var args = CommandLineBuilder.WindowsQuotedArguments("arg1", "path with spaces", "normal");
// Returns: arg1 "path with spaces" normal

// Quote for cmd.exe (handles special characters like &, |, ^, etc.)
var cmdArg = CommandLineBuilder.WindowsCmdArgument(@"malicious argument"" & whoami");
// Returns properly escaped argument safe for cmd.exe

var cmdArgs = CommandLineBuilder.WindowsCmdArguments("echo", "Hello & Goodbye");
// Returns arguments safe for cmd.exe execution
```

**Why use CommandLineBuilder?**
- Handles spaces, quotes, and backslashes correctly
- Prevents command injection attacks
- `WindowsCmdArgument` handles cmd.exe special characters: `(`, `)`, `%`, `!`, `^`, `"`, `<`, `>`, `&`, `|`
- Based on Microsoft's article: [Everyone quotes command line arguments the wrong way](https://blogs.msdn.microsoft.com/twistylittlepassagesallalike/2011/04/23/everyone-quotes-command-line-arguments-the-wrong-way/)

### Prompt

Create interactive yes/no prompts in console applications.

```csharp
using Meziantou.Framework;

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
