# Meziantou.Framework.Templating.Tool

<!-- help -->
## Help

```
Description:
  Render template files using Meziantou.Framework.Templating

Usage:
  Meziantou.Framework.Templating.Tool [options]

Options:
  --input <input> (REQUIRED)                                 Path to an input template file, or a glob pattern (*, ?, **) that matches template files. Can be repeated
  --output <output>                                          Path to the output file of a single template. If omitted, writes next to the template when its name is known, or to stdout
  --output-directory <output-directory>                      Directory of the generated files. A template matched by a glob pattern keeps its path relative to the start of the pattern
  --output-extension <output-extension>                      Extension of the generated file of a template that has no output directive
  --root-directory <root-directory>                          Directory the input paths and glob patterns are relative to. Defaults to the current directory
  --arg <arg>                                                Argument passed to the templates, as name=value. The value is a string. Can be repeated
  --arguments-file <arguments-file>                          Path to a JSON file whose root object holds the arguments passed to the templates
  --start-code-block-delimiter <start-code-block-delimiter>  Delimiter that marks the start of a code block
  --end-code-block-delimiter <end-code-block-delimiter>      Delimiter that marks the end of a code block
  --output-encoding <output-encoding>                        Output encoding for generated files. Example: utf8, utf8-bom, unicode, utf-16, utf-32
  --line-ending <line-ending>                                Line ending to use in output. Values: LF, CRLF, CR
  -?, -h, --help                                             Show help and usage information
  --version                                                  Show version information
```
<!-- help -->