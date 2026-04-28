# Meziantou.Framework.Html.Tool

<!-- help -->
## Help

```
Description:

Usage:
  meziantou.html [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  replace-value     Replace element/attribute values in an html file
  append-version    Append version to style / script URLs
  inline-resources  Inline scripts, styles, and images
```

### append-version

```
Description:
  Append version to style / script URLs

Usage:
  meziantou.html append-version [options]

Options:
  --single-file <single-file>        Path of the file to update
  --file-pattern <file-pattern>      Glob pattern to find files to update
  --root-directory <root-directory>  Root directory for glob pattern
  -?, -h, --help                     Show help and usage information
```

### inline-resources

```
Description:
  Inline scripts, styles, and images

Usage:
  meziantou.html inline-resources [options]

Options:
  --single-file <single-file>                         Path of the file to update
  --file-pattern <file-pattern>                       Glob pattern to find files to update
  --root-directory <root-directory>                   Root directory for glob pattern
  --resource-patterns <resource-patterns> (REQUIRED)  Files to inline
  -?, -h, --help                                      Show help and usage information
```

### replace-value

```
Description:
  Replace element/attribute values in an html file

Usage:
  meziantou.html replace-value [options]

Options:
  --single-file <single-file>         Path of the file to update
  --file-pattern <file-pattern>       Glob pattern to find files to update
  --root-directory <root-directory>   Root directory for glob pattern
  --xpath <xpath> (REQUIRED)          XPath to the elements/attributes to replace
  --new-value <new-value> (REQUIRED)  New value for the elements/attributes
  -?, -h, --help                      Show help and usage information
```
<!-- help -->