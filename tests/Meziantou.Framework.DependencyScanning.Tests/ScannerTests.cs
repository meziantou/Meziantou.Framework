#pragma warning disable MA0101
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Scanners;
using Meziantou.Framework.Globbing;
using Meziantou.Xunit;

namespace Meziantou.Framework.DependencyScanning.Tests;

public sealed partial class ScannerTests(ITestOutputHelper testOutputHelper) : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    [Fact]
    public async Task PythonProjectDependencies()
    {
        AddFile("pyproject.toml", """
            [project]
            name = "my-app"
            version = "0.1.0"
            dependencies = [
                "requests==2.31.0",
            ]

            [tool.poetry.dependencies]
            python = ">=3.12"
            httpx = "0.27.0"
            """);
        AddFile("poetry.lock", """
            [[package]]
            name = "requests"
            version = "2.31.0"
            """);

        var result = await GetDependencies<PythonProjectDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.PyPi, "requests", "2.31.0", 5, 16),
            (DependencyType.PyPi, "httpx", "0.27.0", 10, 10));
        Assert.Single(result, d => d.Name == "requests" && d.VersionLocation is { IsUpdatable: false } location && location.FilePath.EndsWith("poetry.lock", StringComparison.Ordinal));
        Assert.DoesNotContain(result, d => d.Name is "name" or "version");
    }

    [Fact]
    public async Task PythonPipfileDependencies()
    {
        AddFile("Pipfile", """
            [[source]]
            url = "https://pypi.org/simple"
            verify_ssl = true
            name = "pypi"

            [packages]
            requests = "==2.31.0"

            [dev-packages]
            pytest = "8.0.0"

            [requires]
            python_version = "3.11"
            """);
        AddFile("Pipfile.lock", /*lang=json,strict*/ """
            {
              "default": {
                "requests": { "version": "==2.31.0" }
              }
            }
            """);

        var result = await GetDependencies<PythonProjectDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.PyPi, "requests", "2.31.0", 7, 15),
            (DependencyType.PyPi, "pytest", "8.0.0", 10, 11),
            (DependencyType.PyPi, "requests", "2.31.0", 0, 0));
        Assert.DoesNotContain(result, d => d.Version is not null && d.Version.StartsWith('=', StringComparison.Ordinal));
        Assert.DoesNotContain(result, d => d.Name is "url" or "verify_ssl" or "name" or "python_version");
    }

    [Fact]
    public async Task ComposerDependencies()
    {
        AddFile("composer.json", """
            {
              "require": {
                "php": ">=8.1",
                "ext-json": "*",
                "monolog/monolog": "^3.0"
              },
              "require-dev": {
                "phpunit/phpunit": "^11.0"
              }
            }
            """);
        AddFile("composer.lock", """
            {
              "packages": [
                { "name": "monolog/monolog", "version": "3.0.0" }
              ],
              "packages-dev": [
                { "name": "phpunit/phpunit", "version": "11.0.0" }
              ]
            }
            """);

        var result = await GetDependencies<ComposerDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.PhpPackage, "monolog/monolog", "^3.0", 0, 0),
            (DependencyType.PhpPackage, "phpunit/phpunit", "^11.0", 0, 0));
        Assert.DoesNotContain(result, d => d.Name is "php" or "ext-json");
    }

    [Fact]
    public async Task JavaDependencies()
    {
        AddFile("pom.xml", """
            <project>
              <groupId>com.acme</groupId>
              <artifactId>app</artifactId>
              <version>1.0-SNAPSHOT</version>
              <parent>
                <groupId>org.example</groupId>
                <artifactId>example-parent</artifactId>
                <version>2.0.0</version>
              </parent>
              <dependencies>
                <dependency>
                  <groupId>org.example</groupId>
                  <artifactId>example-bom-managed</artifactId>
                </dependency>
                <dependency>
                  <groupId>org.example</groupId>
                  <artifactId>example-core</artifactId>
                  <version>1.2.3</version>
                  <exclusions>
                    <exclusion>
                      <groupId>org.excluded</groupId>
                      <artifactId>excluded</artifactId>
                    </exclusion>
                  </exclusions>
                </dependency>
                <dependency>
                  <groupId>org.example</groupId>
                  <artifactId>example-property</artifactId>
                  <version>${example.version}</version>
                </dependency>
              </dependencies>
              <build>
                <plugins>
                  <plugin>
                    <artifactId>maven-compiler-plugin</artifactId>
                    <version>3.11.0</version>
                  </plugin>
                </plugins>
              </build>
            </project>
            """);
        AddFile("build.gradle", """
            dependencies {
                implementation "org.example:example-core:1.2.3"
                implementation 'org.example:example-aar:1.0.0@aar'
                implementation "org.example:example-interpolated:$exampleVersion"
            }
            """);

        var result = await GetDependencies<JavaDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.JavaPackage, "org.example:example-parent", "2.0.0", 8, 14),
            (DependencyType.JavaPackage, "org.example:example-core", "1.2.3", 18, 16),
            (DependencyType.JavaPackage, "org.example:example-property", "${example.version}", 0, 0),
            (DependencyType.JavaPackage, "org.apache.maven.plugins:maven-compiler-plugin", "3.11.0", 36, 18),
            (DependencyType.JavaPackage, "org.example:example-core", "1.2.3", 2, 46),
            (DependencyType.JavaPackage, "org.example:example-aar", "1.0.0", 3, 45));
        Assert.False(Assert.Single(result, d => d.Name == "org.example:example-property").VersionLocation!.IsUpdatable);
        Assert.DoesNotContain(result, d => d.Name is "com.acme:app" or "org.excluded:excluded" or "org.example:maven-compiler-plugin" or "org.example:example-interpolated");
    }

    [Fact]
    public async Task JavaVersionCatalogDependencies()
    {
        AddFile("gradle/libs.versions.toml", """
            [versions]
            guava = "33.0.0"
            shared = "1.0.0"

            [libraries]
            guava = { module = "com.google.guava:guava", version.ref = "guava" }
            commons = "org.apache.commons:commons-lang3:3.14.0"
            junit = { group = "org.junit.jupiter", name = "junit-jupiter", version = "5.10.0" }
            a = { module = "org.example:a", version.ref = "shared" }
            b = { module = "org.example:b", version.ref = "shared" }

            [plugins]
            kotlin = { id = "org.jetbrains.kotlin.jvm", version.ref = "kotlin" }
            """);

        var result = await GetDependencies<JavaDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.JavaPackage, "com.google.guava:guava", "33.0.0", 2, 10),
            (DependencyType.JavaPackage, "org.apache.commons:commons-lang3", "3.14.0", 7, 45),
            (DependencyType.JavaPackage, "org.junit.jupiter:junit-jupiter", "5.10.0", 8, 75),
            (DependencyType.JavaPackage, "org.example:a", "1.0.0", 0, 0),
            (DependencyType.JavaPackage, "org.example:b", "1.0.0", 0, 0));
        Assert.False(Assert.Single(result, d => d.Name == "org.example:a").VersionLocation!.IsUpdatable);
        Assert.Equal(5, result.Count(d => d.Type == DependencyType.JavaPackage));
    }

    [Fact]
    public async Task GoModuleDependencies()
    {
        const string Original = """
            module example.com/app

            require github.com/foo/bar v1.2.3

            require (
                golang.org/x/text v0.14.0 // indirect
                example.com/local v0.1.0
            )
            """;
        const string Expected = """
            module example.com/app

            require dummy1 v2.0.0

            require (
                dummy2 v2.0.0 // indirect
                dummy3 v2.0.0
            )
            """;

        AddFile("go.mod", Original);
        var result = await GetDependencies<GoModuleDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.GoModule, "github.com/foo/bar", "v1.2.3", 3, 28),
            (DependencyType.GoModule, "golang.org/x/text", "v0.14.0", 6, 23),
            (DependencyType.GoModule, "example.com/local", "v0.1.0", 7, 23));

        await UpdateDependencies(result, "dummy", "v2.0.0");
        AssertFileContentEqual("go.mod", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task GoModuleSumDependencies()
    {
        AddFile("go.sum", """
            github.com/foo/bar v1.2.3 h1:abc
            github.com/foo/bar v1.2.3/go.mod h1:def
            golang.org/x/text v0.14.0 h1:ghi
            """);

        var result = await GetDependencies<GoModuleDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.GoModule, "github.com/foo/bar", "v1.2.3", 0, 0),
            (DependencyType.GoModule, "golang.org/x/text", "v0.14.0", 0, 0));
        Assert.DoesNotContain(result, d => d.VersionLocation!.IsUpdatable);
    }

    [Fact]
    public async Task CargoDependencies()
    {
        const string Original = """
            [dependencies]
            serde = "1.0"
            tokio = { version = "1.0", features = ["full"] }
            local = { path = "../local" }
            reqwest = { features = ["json"], version = "0.12" }
            renamed = { package = "foo-version", version = "2" }
            git = { git = "https://github.com/a/version-lib", branch = "main" }
            literal = '1.5'
            dotted.version = "0.3"
            dotted.features = ["a"]
            member.workspace = true

            [dev-dependencies]
            anyhow = "1.0"
            """;
        const string Expected = """
            [dependencies]
            dummy1 = "2.0.0"
            dummy2 = { version = "2.0.0", features = ["full"] }
            dummy3 = { path = "../local" }
            dummy4 = { features = ["json"], version = "2.0.0" }
            dummy5 = { package = "foo-version", version = "2.0.0" }
            dummy6 = { git = "https://github.com/a/version-lib", branch = "main" }
            dummy7 = '2.0.0'
            dummy8.version = "2.0.0"
            dotted.features = ["a"]
            member.workspace = true

            [dev-dependencies]
            dummy9 = "2.0.0"
            """;

        AddFile("Cargo.toml", Original);
        var result = await GetDependencies<CargoDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.RustCrate, "serde", "1.0", 2, 10),
            (DependencyType.RustCrate, "tokio", "1.0", 3, 22),
            (DependencyType.RustCrate, "local", null, 0, 0),
            (DependencyType.RustCrate, "reqwest", "0.12", 5, 45),
            (DependencyType.RustCrate, "renamed", "2", 6, 49),
            (DependencyType.RustCrate, "git", null, 0, 0),
            (DependencyType.RustCrate, "literal", "1.5", 8, 12),
            (DependencyType.RustCrate, "dotted", "0.3", 9, 19),
            (DependencyType.RustCrate, "anyhow", "1.0", 14, 11));
        Assert.Equal(9, result.Count(d => d.Type == DependencyType.RustCrate));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Cargo.toml", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task CargoDependencies_WithAnUnterminatedString()
    {
        AddFile("Cargo.toml", """
            [dependencies]
            tokio = "1.0
            serde = "
            """);

        var result = await GetDependencies<CargoDependencyScanner>();

        AssertContainDependency(result,
            (DependencyType.RustCrate, "tokio", null, 0, 0),
            (DependencyType.RustCrate, "serde", null, 0, 0));
    }

    [Fact]
    public async Task CargoDependencies_InEveryFormOfTheTable()
    {
        const string Original = """
            dependencies.anyhow = "1.0"

            [dependencies.tokio]
            version = "1.2"
            features = ["full"]

            [target.'cfg(windows)'.dependencies]
            winapi = "0.3"

            [workspace.dependencies]
            serde = { version = "1.0" }

            [build-dependencies.cc]
            path = "../cc"
            """;
        const string Expected = """
            dependencies.dummy1 = "2.0.0"

            [dependencies.dummy2]
            version = "2.0.0"
            features = ["full"]

            [target.'cfg(windows)'.dependencies]
            dummy3 = "2.0.0"

            [workspace.dependencies]
            dummy4 = { version = "2.0.0" }

            [build-dependencies.cc]
            path = "../cc"
            """;

        AddFile("Cargo.toml", Original);
        var result = await GetDependencies<CargoDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.RustCrate, "anyhow", "1.0", 1, 24),
            (DependencyType.RustCrate, "tokio", "1.2", 4, 12),
            (DependencyType.RustCrate, "winapi", "0.3", 8, 11),
            (DependencyType.RustCrate, "serde", "1.0", 11, 22));
        Assert.Equal(4, result.Count(d => d.Type == DependencyType.RustCrate));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Cargo.toml", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task CargoDependencies_WithEscapeSequences_AreNotUpdatable()
    {
        AddFile("Cargo.toml", """
            [dependencies]
            serde = "1\u002E0"
            "tok\u0069o" = "1.2"
            """);

        var result = await GetDependencies<CargoDependencyScanner>();

        Assert.False(Assert.Single(result, d => d.Name == "serde" && d.Version == "1.0").VersionLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Name == "tokio" && d.Version == "1.2").NameLocation!.IsUpdatable);
    }

    [Fact]
    public async Task JavaVersionCatalogDependencies_WithEscapeSequences_AreNotUpdatable()
    {
        AddFile("gradle/libs.versions.toml", """
            [versions]
            shared = "2\u002E0"

            [libraries]
            guava = "com.google\u002Eguava:guava:33.0.0"
            x = { module = "org.example:x", version = "1\u002E0" }
            y = { module = "org.example:y", version.ref = "shared" }
            """);

        var result = await GetDependencies<JavaDependencyScanner>();

        Assert.False(Assert.Single(result, d => d.Name == "com.google.guava:guava" && d.Version == "33.0.0").VersionLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Name == "org.example:x" && d.Version == "1.0").VersionLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Name == "org.example:y" && d.Version == "2.0").VersionLocation!.IsUpdatable);
    }

    [Fact]
    public async Task CargoLockDependencies()
    {
        AddFile("Cargo.lock", """
            version = 4

            [[package]]
            name = "serde"
            version = "1.0.0"
            source = "registry+https://github.com/rust-lang/crates.io-index"

            [[package]]
            name = "serde_derive"
            version = "1.0.0"
            """);

        var result = await GetDependencies<CargoDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.RustCrate, "serde", "1.0.0", 0, 0),
            (DependencyType.RustCrate, "serde_derive", "1.0.0", 0, 0));
        Assert.DoesNotContain(result, d => d.VersionLocation!.IsUpdatable);
    }

    [Fact]
    public async Task RubyGemDependencies()
    {
        const string Original = """
            source "https://rubygems.org"
            gem "rails", "~> 7.0"
            gem 'without-version'
            """;
        const string Expected = """
            source "https://rubygems.org"
            gem "dummy1", "2.0.0"
            gem 'dummy2'
            """;

        AddFile("Gemfile", Original);
        var result = await GetDependencies<RubyGemDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.RubyGem, "rails", "~> 7.0", 2, 15),
            (DependencyType.RubyGem, "without-version", null, 0, 0));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Gemfile", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task RubyGemDependencies_GemspecAndLockFile()
    {
        AddFile("sample.gemspec", """
            Gem::Specification.new do |spec|
              spec.add_dependency "rack", "3.0.0"
              spec.add_runtime_dependency("puma", "~> 6.0")
              spec.add_development_dependency "rspec"
            end
            """);
        AddFile("Gemfile.lock", """
            GEM
              specs:
                rack (3.0.0)
                  base64
                puma (6.0.1)

            DEPENDENCIES
              rack
            """);

        var gemspecResult = await GetDependencies<RubyGemDependencyScanner>();
        AssertContainDependency(gemspecResult,
            (DependencyType.RubyGem, "rack", "3.0.0", 2, 32),
            (DependencyType.RubyGem, "puma", "~> 6.0", 3, 40),
            (DependencyType.RubyGem, "rspec", null, 0, 0));

        var lockPath = _directory.GetFullPath("Gemfile.lock");
        var lockResult = await DependencyScanner.ScanFileAsync(_directory.FullPath, lockPath, await File.ReadAllBytesAsync(lockPath), [new RubyGemDependencyScanner()], XunitCancellationToken);
        AssertContainDependency(lockResult,
            (DependencyType.RubyGem, "rack", "3.0.0", 0, 0),
            (DependencyType.RubyGem, "puma", "6.0.1", 0, 0));
        Assert.DoesNotContain(lockResult, d => d.VersionLocation!.IsUpdatable);
    }

    [Fact]
    public async Task NpmPackageJsonDependencies()
    {
        const string Original = /*lang=json,strict*/ """
{
  "name": "sample",
  "version": "0.1.0",
  "dependencies": {
    "a": "1.0.0"
  },
  "devDependencies": {
    "b": "1.2.3",
    "c": null
  },
  "peerDependencies": {
    "d": "3.1.4"
  },
  "optionalDependencies": {
    "e": "4.1.5"
  }
}
""";

        const string Expected = /*lang=json,strict*/ """
{
  "name": "sample",
  "version": "0.1.0",
  "dependencies": {
    "a": "2.0.0"
  },
  "devDependencies": {
    "b": "2.0.0",
    "c": null
  },
  "peerDependencies": {
    "d": "2.0.0"
  },
  "optionalDependencies": {
    "e": "2.0.0"
  }
}
""";

        AddFile("package.json", Original);
        var result = await GetDependencies<NpmPackageJsonDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.Npm, "a", "1.0.0", 0, 0),
            (DependencyType.Npm, "b", "1.2.3", 0, 0),
            (DependencyType.Npm, "d", "3.1.4", 0, 0),
            (DependencyType.Npm, "e", "4.1.5", 0, 0));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("package.json", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task NpmPackageJsonDependencies_ProtocolsAndAliases()
    {
        const string Original = /*lang=json,strict*/ """
{
  "dependencies": {
    "a": "1.0.0",
    "b": "npm:real-b@^2.0.0",
    "c": "npm:@scope/real-c@3.0.0",
    "d": "workspace:*",
    "e": "file:../e",
    "f": "link:../f",
    "g": "portal:../g",
    "h": "git+https://github.com/owner/h.git#v1",
    "i": "github:owner/i#v1",
    "j": "https://example.com/j.tgz",
    "k": "owner/k#v1"
  }
}
""";
        const string Expected = /*lang=json,strict*/ """
{
  "dependencies": {
    "a": "4.0.0",
    "b": "npm:real-b@4.0.0",
    "c": "npm:@scope/real-c@4.0.0",
    "d": "workspace:*",
    "e": "file:../e",
    "f": "link:../f",
    "g": "portal:../g",
    "h": "git+https://github.com/owner/h.git#v1",
    "i": "github:owner/i#v1",
    "j": "https://example.com/j.tgz",
    "k": "owner/k#v1"
  }
}
""";

        AddFile("package.json", Original);
        var result = await GetDependencies<NpmPackageJsonDependencyScanner>();
        Assert.HasCount(11, result);
        Assert.True(Assert.Single(result, d => d.Name == "a").VersionLocation!.IsUpdatable);

        var realB = Assert.Single(result, d => d.Name == "real-b");
        Assert.Equal("^2.0.0", realB.Version);
        Assert.Equal("b", realB.Metadata["alias"]);
        Assert.True(realB.VersionLocation!.IsUpdatable);

        var realC = Assert.Single(result, d => d.Name == "@scope/real-c");
        Assert.Equal("3.0.0", realC.Version);
        Assert.Equal("c", realC.Metadata["alias"]);

        foreach (var name in new[] { "d", "e", "f", "g", "h", "i", "j", "k" })
        {
            var dependency = Assert.Single(result, d => d.Name == name);
            Assert.False(dependency.VersionLocation!.IsUpdatable);
        }

        await UpdateDependencies(result, "dummy", "4.0.0");
        AssertFileContentEqual("package.json", Expected, ignoreNewLines: true);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    public async Task NpmPackageJsonDependencies_Encodings(string encodingName)
    {
        Encoding encoding = encodingName switch
        {
            "utf-8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "utf-8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "utf-16le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            _ => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        };

        const string Content = /*lang=json,strict*/ """
{
  "dependencies": {
    "a": "1.0.0"
  }
}
""";
        const string Expected = /*lang=json,strict*/ """
{
  "dependencies": {
    "a": "2.0.0"
  }
}
""";
        AddFile("package.json", [.. encoding.GetPreamble(), .. encoding.GetBytes(Content)]);

        var result = await GetDependencies<NpmPackageJsonDependencyScanner>();
        AssertContainDependency(result, (DependencyType.Npm, "a", "1.0.0", 0, 0));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("package.json", Expected, ignoreNewLines: true);
    }

    [Theory]
    [InlineData("""{"dependencies":{"a":"1.0.0","a":"2.0.0"}}""")]
    [InlineData("""{"dependencies":{"a":"1.0.0"},"dependencies":{"b":"2.0.0"}}""")]
    [InlineData("""{"dependencies":{"a":"\uD800"}}""")]
    public async Task NpmPackageJsonDependencies_MalformedJson_IsSkipped(string content)
    {
        AddFile("package.json", content);

        var result = await GetDependencies<NpmPackageJsonDependencyScanner>();

        Assert.Empty(result);
    }

    [Fact]
    public async Task NpmPackageJsonDependencies_InvalidUtf8_IsSkipped()
    {
        AddFile("package.json", [.. """{"dependencies":{"a":"1.0."""u8, 0xE9, .. "\"}}"u8]);

        var result = await GetDependencies<NpmPackageJsonDependencyScanner>();

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd")]
    [InlineData("http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd")]
    [InlineData("http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd")]
    [InlineData("http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd")]
    [InlineData("http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd")]
    [InlineData("http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd")]
    public async Task NuSpecDependencies(string xmlns)
    {
        var original = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="{xmlns}">
                <metadata>
                    <id>sample</id>
                    <version>1.0.0</version>
                    <authors>Microsoft</authors>
                    <dependencies>
                        <dependency id="another-package" version="3.0.0" />
                        <dependency id="yet-another-package" version="1.0.0" />
                    </dependencies>
                </metadata>
            </package>
            """;

        var expected = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="{xmlns}">
                <metadata>
                    <id>sample</id>
                    <version>1.0.0</version>
                    <authors>Microsoft</authors>
                    <dependencies>
                        <dependency id="dummy1" version="2.0.0" />
                        <dependency id="dummy2" version="2.0.0" />
                    </dependencies>
                </metadata>
            </package>
            """;

        AddFile("test.nuspec", original);
        var result = await GetDependencies<NuSpecDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "another-package", "3.0.0", 8, 46),
            (DependencyType.NuGet, "yet-another-package", "1.0.0", 9, 50));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("test.nuspec", expected, ignoreNewLines: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" xmlns=\"urn:unknown\"")]
    public async Task NuSpecDependencies_AnyRootNamespace(string xmlns)
    {
        var original = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package{xmlns}>
                <metadata>
                    <id>sample</id>
                    <dependencies>
                        <dependency id="another-package" version="3.0.0" />
                    </dependencies>
                </metadata>
            </package>
            """;

        var expected = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package{xmlns}>
                <metadata>
                    <id>sample</id>
                    <dependencies>
                        <dependency id="dummy1" version="2.0.0" />
                    </dependencies>
                </metadata>
            </package>
            """;

        AddFile("test.nuspec", original);
        var result = await GetDependencies<NuSpecDependencyScanner>();
        AssertContainDependency(result, (DependencyType.NuGet, "another-package", "3.0.0", 6, 46));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("test.nuspec", expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task NuSpecDependencies_ReplacementTokensAreNotUpdatable()
    {
        const string Original = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                <metadata>
                    <dependencies>
                        <dependency id="PackageA" version="$version$" />
                        <dependency id="PackageB" version="[$version$]" />
                        <dependency id="PackageC" version="1.0.0" />
                        <dependency id="$id$" version="1.0.0" />
                    </dependencies>
                </metadata>
            </package>
            """;

        const string Expected = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                <metadata>
                    <dependencies>
                        <dependency id="dummy1" version="$version$" />
                        <dependency id="dummy2" version="[$version$]" />
                        <dependency id="dummy3" version="2.0.0" />
                        <dependency id="$id$" version="2.0.0" />
                    </dependencies>
                </metadata>
            </package>
            """;

        AddFile("test.nuspec", Original);
        var result = await GetDependencies<NuSpecDependencyScanner>();
        Assert.HasCount(4, result);
        Assert.False(Assert.Single(result, d => d.Name == "$id$").NameLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Name == "PackageA" && d.Version == "$version$").VersionLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Name == "PackageB" && d.Version == "[$version$]").VersionLocation!.IsUpdatable);
        Assert.True(Assert.Single(result, d => d.Name == "PackageC" && d.Version == "1.0.0").VersionLocation!.IsUpdatable);

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("test.nuspec", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task MsBuildReferencesDependencies()
    {
        const string Original = """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>Sample</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
      <PackageVersion Include="PackageA" Version="1.0.0" />
  </ItemGroup>

  <!-- Comment -->
  <ItemGroup>
    <PackageReference Include="TestPackage" Version="4.2.1" />
    <PackageReference Include="PackageA" VersionOverride="1.2.1" />
    <PackageDownload Include="PackageC" Version="1.2.2" />
    <GlobalPackageReference Include="PackageD" Version="1.2.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="ProjectA.csproj" />
    <PackageReference Include="PackageE" />
  </ItemGroup>
</Project>
""";
        const string Expected = """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>Sample</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
      <PackageVersion Include="dummy1" Version="2.0.0" />
  </ItemGroup>

  <!-- Comment -->
  <ItemGroup>
    <PackageReference Include="dummy2" Version="2.0.0" />
    <PackageReference Include="dummy3" VersionOverride="2.0.0" />
    <PackageDownload Include="dummy4" Version="2.0.0" />
    <GlobalPackageReference Include="dummy5" Version="2.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="dummy6" />
    <PackageReference Include="dummy7" />
  </ItemGroup>
</Project>
""";

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "PackageA", "1.0.0", 8, 42),
            (DependencyType.NuGet, "PackageA", "1.2.1", 14, 42),
            (DependencyType.NuGet, "TestPackage", "4.2.1", 13, 45),
            (DependencyType.NuGet, "PackageC", "1.2.2", 15, 41),
            (DependencyType.NuGet, "PackageD", "1.2.3", 16, 48),
            (DependencyType.MSBuildProjectReference, "ProjectA.csproj", null, 0, 0));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Theory]
    [InlineData("test.fsproj")]
    [InlineData("test.vbproj")]
    [InlineData("build.proj")]
    public async Task MsBuildReferencesDependencies_ProjectFileExtensions(string fileName)
    {
        AddFile(fileName, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="TestPackage" Version="4.2.1" />
              </ItemGroup>
            </Project>
            """);

        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();

        AssertContainDependency(result, (DependencyType.NuGet, "TestPackage", "4.2.1", 3, 45));
    }

    [Fact]
    public async Task MsBuildReferencesDependencies_UpdateItems()
    {
        const string Original = """
            <Project>
              <ItemGroup>
                <PackageReference Update="PackageA" Version="1.0.0" />
                <PackageVersion Update="PackageB" Version="1.0.1" />
                <PackageDownload Update="PackageC" Version="1.0.2" />
                <GlobalPackageReference Update="PackageD" Version="1.0.3" />
                <PackageReference Update="PackageE">
                  <Version>1.0.4</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;
        const string Expected = """
            <Project>
              <ItemGroup>
                <PackageReference Update="dummy1" Version="2.0.0" />
                <PackageVersion Update="dummy2" Version="2.0.0" />
                <PackageDownload Update="dummy3" Version="2.0.0" />
                <GlobalPackageReference Update="dummy4" Version="2.0.0" />
                <PackageReference Update="dummy5">
                  <Version>2.0.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        Assert.HasCount(5, result);
        AssertContainDependency(result,
            (DependencyType.NuGet, "PackageA", "1.0.0", 3, 41),
            (DependencyType.NuGet, "PackageB", "1.0.1", 4, 39),
            (DependencyType.NuGet, "PackageC", "1.0.2", 5, 40),
            (DependencyType.NuGet, "PackageD", "1.0.3", 6, 47),
            (DependencyType.NuGet, "PackageE", "1.0.4", 8, 0));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task MsBuildReferencesDependencies_MsBuildExpressionsAreNotUpdatable()
    {
        const string Original = """
            <Project>
              <PropertyGroup>
                <TargetFramework>$(DefaultTargetFramework)</TargetFramework>
                <TargetFrameworks>$(TargetFrameworks);net48</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="PackageA" Version="$(XunitVersion)" />
                <PackageReference Include="PackageB" Version="1.0.$(Patch)" />
                <PackageReference Include="PackageC" VersionOverride="$(PackageCVersion)" />
                <PackageReference Include="PackageD">
                  <Version>$(PackageDVersion)</Version>
                </PackageReference>
                <PackageReference Include="$(PackageEName)" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """;
        const string Expected = """
            <Project>
              <PropertyGroup>
                <TargetFramework>$(DefaultTargetFramework)</TargetFramework>
                <TargetFrameworks>$(TargetFrameworks);2.0.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="dummy1" Version="$(XunitVersion)" />
                <PackageReference Include="dummy2" Version="1.0.$(Patch)" />
                <PackageReference Include="dummy3" VersionOverride="$(PackageCVersion)" />
                <PackageReference Include="dummy4">
                  <Version>$(PackageDVersion)</Version>
                </PackageReference>
                <PackageReference Include="$(PackageEName)" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """;

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        Assert.HasCount(8, result);
        Assert.False(Assert.Single(result, d => d.Name == "$(PackageEName)" && d.Version == "1.0.0").NameLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Name == "PackageA" && d.Version == "$(XunitVersion)").VersionLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Name == "PackageB" && d.Version == "1.0.$(Patch)").VersionLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Name == "PackageC" && d.Version == "$(PackageCVersion)").VersionLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Name == "PackageD" && d.Version == "$(PackageDVersion)").VersionLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Type == DependencyType.DotNetTargetFramework && d.Version == "$(DefaultTargetFramework)").VersionLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Type == DependencyType.DotNetTargetFramework && d.Version == "$(TargetFrameworks)").VersionLocation!.IsUpdatable);
        Assert.True(Assert.Single(result, d => d.Type == DependencyType.DotNetTargetFramework && d.Version == "net48").VersionLocation!.IsUpdatable);

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task MsBuildSdkReferencesDependencies()
    {
        const string Original = """
            <Project Sdk="MSBuild.Sdk.Extras/2.0.54">
                <Sdk Name="My.Custom.Sdk1" Version="1.0.0" />
                <Import Sdk="My.Custom.Sdk2/2.0.55" />
            </Project>
            """;
        const string Expected = """
            <Project Sdk="dummy1/1.2.3">
                <Sdk Name="dummy2" Version="1.2.3" />
                <Import Sdk="dummy3/1.2.3" />
            </Project>
            """;

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        await UpdateDependencies(result, "dummy", "1.2.3");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task MsBuildSdkReferencesDependencies_SdkListsAndImportVersion()
    {
        const string Original = """
            <Project Sdk="SdkA/1.0.0; SdkB/2.0.0 ;SdkC;SdkD/min=4.0.0">
                <Import Sdk="SdkE" Version="5.0.0" Project="Sdk.props" />
                <Import Sdk="SdkF" MinimumVersion="6.0.0" Project="Sdk.props" />
                <Import Sdk="SdkG/7.0.0;SdkH/8.0.0" Project="Sdk.targets" />
            </Project>
            """;
        const string Expected = """
            <Project Sdk="dummy1/1.2.3; dummy2/1.2.3 ;SdkC;SdkD/min=4.0.0">
                <Import Sdk="dummy3" Version="1.2.3" Project="Sdk.props" />
                <Import Sdk="SdkF" MinimumVersion="6.0.0" Project="Sdk.props" />
                <Import Sdk="dummy4/1.2.3;dummy5/1.2.3" Project="Sdk.targets" />
            </Project>
            """;

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        Assert.HasCount(5, result);
        AssertContainDependency(result,
            (DependencyType.NuGet, "SdkA", "1.0.0", 1, 0),
            (DependencyType.NuGet, "SdkB", "2.0.0", 1, 0),
            (DependencyType.NuGet, "SdkE", "5.0.0", 2, 24),
            (DependencyType.NuGet, "SdkG", "7.0.0", 4, 0),
            (DependencyType.NuGet, "SdkH", "8.0.0", 4, 0));

        await UpdateDependencies(result, "dummy", "1.2.3");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task PackagesReferencesWithNamespaceDependencies()
    {
        const string Original = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project Sdk="Microsoft.NET.Sdk" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">

              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <RootNamespace>Sample</RootNamespace>
              </PropertyGroup>

                <!-- Comment -->
              <ItemGroup>
                <PackageReference Include="TestPackage" Version="4.2.1" />
              </ItemGroup>

            </Project>

            """;
        const string Expected = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project Sdk="Microsoft.NET.Sdk" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">

              <PropertyGroup>
                <TargetFramework>2.0.0</TargetFramework>
                <RootNamespace>Sample</RootNamespace>
              </PropertyGroup>

                <!-- Comment -->
              <ItemGroup>
                <PackageReference Include="dummy1" Version="2.0.0" />
              </ItemGroup>

            </Project>

            """;

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "TestPackage", "4.2.1", 11, 45),
            (DependencyType.DotNetTargetFramework, null, "netstandard2.0", 0, 0));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task MsBuildSdkReference_FileShrankSinceScan()
    {
        AddFile("test.csproj", """
            <Project Sdk="My.Sdk/1.2.3" />
            """);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        var dependency = Assert.Single(result, d => d.Name == "My.Sdk");

        // The recorded location points past the end of the shortened attribute value
        AddFile("test.csproj", """
            <Project Sdk="A" />
            """);

        await Assert.ThrowsAsync<DependencyScannerException>(() => dependency.UpdateVersionAsync("2.0.0"));
    }

    [Fact]
    public async Task DotNetTargetFrameworkWithNamespace()
    {
        const string Original = """
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
    <PropertyGroup>
        <TargetFramework>net472</TargetFramework>
        <TargetFrameworks>net48</TargetFrameworks>
        <TargetFrameworks>net5.0;net6.0</TargetFrameworks>
        <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    </PropertyGroup>
</Project>
""";
        const string Expected = """
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
    <PropertyGroup>
        <TargetFramework>net0.0</TargetFramework>
        <TargetFrameworks>net0.0</TargetFrameworks>
        <TargetFrameworks>net0.0;net0.0</TargetFrameworks>
        <TargetFrameworkVersion>net0.0</TargetFrameworkVersion>
    </PropertyGroup>
</Project>
""";

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.DotNetTargetFramework, null, "net472", 0, 0),
            (DependencyType.DotNetTargetFramework, null, "v4.7.2", 0, 0));

        await UpdateDependencies(result, "dummy", "net0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task DotNetTargetFramework()
    {
        const string Original = """
<Project>
    <PropertyGroup>
        <TargetFramework>net472</TargetFramework>
        <TargetFrameworks>net48</TargetFrameworks>
        <TargetFrameworks>net5.0;net6.0</TargetFrameworks>
        <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    </PropertyGroup>
</Project>
""";
        const string Expected = """
<Project>
    <PropertyGroup>
        <TargetFramework>net0.0</TargetFramework>
        <TargetFrameworks>net0.0</TargetFrameworks>
        <TargetFrameworks>net0.0;net0.0</TargetFrameworks>
        <TargetFrameworkVersion>net0.0</TargetFrameworkVersion>
    </PropertyGroup>
</Project>
""";

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        await UpdateDependencies(result, "dummy", "net0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task DotNetTargetFramework_WhitespaceAndEmptyEntries()
    {
        const string Original = """
            <Project>
                <PropertyGroup>
                    <TargetFramework> net8.0 </TargetFramework>
                    <TargetFramework />
                    <TargetFramework>   </TargetFramework>
                    <TargetFrameworks> net5.0;;net6.0; </TargetFrameworks>
                    <TargetFrameworks>;</TargetFrameworks>
                    <TargetFrameworkVersion> v4.7.2 </TargetFrameworkVersion>
                    <TargetFrameworkVersion></TargetFrameworkVersion>
                </PropertyGroup>
            </Project>
            """;
        const string Expected = """
            <Project>
                <PropertyGroup>
                    <TargetFramework> net0.0 </TargetFramework>
                    <TargetFramework />
                    <TargetFramework>   </TargetFramework>
                    <TargetFrameworks> net0.0;;net0.0; </TargetFrameworks>
                    <TargetFrameworks>;</TargetFrameworks>
                    <TargetFrameworkVersion> net0.0 </TargetFrameworkVersion>
                    <TargetFrameworkVersion></TargetFrameworkVersion>
                </PropertyGroup>
            </Project>
            """;

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        Assert.Equal(["net5.0", "net6.0", "net8.0", "v4.7.2"], result.Select(d => d.Version).Order(StringComparer.Ordinal));

        await UpdateDependencies(result, "dummy", "net0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    // Updating requires XmlLocation to map offsets in the normalized element value onto the raw CRLF text
    [Fact]
    public async Task DotNetTargetFrameworks_MultilineWithCrLf()
    {
        const string Original = "<Project>\r\n  <PropertyGroup>\r\n    <TargetFrameworks>\r\n      net8.0;\r\n      net9.0\r\n    </TargetFrameworks>\r\n  </PropertyGroup>\r\n</Project>\r\n";
        const string Expected = "<Project>\r\n  <PropertyGroup>\r\n    <TargetFrameworks>\r\n      net10.0;\r\n      net10.0\r\n    </TargetFrameworks>\r\n  </PropertyGroup>\r\n</Project>\r\n";

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        Assert.Equal(["net8.0", "net9.0"], result.Select(d => d.Version).Order(StringComparer.Ordinal));

        await UpdateDependencies(result, "dummy", "net10.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task ProjectJsonDependencies()
    {
        const string Original = /*lang=json,strict*/ """
{
  "dependencies": {
    "a": {
      "version": "1.0.1",
      "type": "platform"
    },
    "b": {
      "target": "project"
    },
    "c": "1.0.2"
  },
  "tools": {
    "d": "1.0.3"
  }
}
""";

        const string Expected = /*lang=json,strict*/ """
{
  "dependencies": {
    "a": {
      "version": "2.0.0",
      "type": "platform"
    },
    "b": {
      "target": "project"
    },
    "c": "2.0.0"
  },
  "tools": {
    "d": "2.0.0"
  }
}
""";

        AddFile("project.json", Original);
        var result = await GetDependencies<ProjectJsonDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "a", "1.0.1", 0, 0),
            (DependencyType.NuGet, "c", "1.0.2", 0, 0),
            (DependencyType.NuGet, "d", "1.0.3", 0, 0));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("project.json", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task PythonRequirementsDependencies()
    {
        const string Original = "A==1.1.0\nB==1.2.0\r\nC==1.3.0";
        const string Expected = "dummy1==2.0.0\ndummy2==2.0.0\r\ndummy3==2.0.0";

        AddFile("requirements.txt", Original);
        var result = await GetDependencies<PythonRequirementsDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.PyPi, "A", "1.1.0", 1, 4),
            (DependencyType.PyPi, "B", "1.2.0", 2, 4),
            (DependencyType.PyPi, "C", "1.3.0", 3, 4));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("requirements.txt", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task PythonRequirementsDependencies_RealWorldLines()
    {
        const string Original =
            "requests==2.31.0 # pinned\n" +
            "pkg==1.0 ; python_version<'3.8'\n" +
            "certifi==2024.2.2 \\\n" +
            "    --hash=sha256:abc \\\n" +
            "    --hash=sha256:def\n" +
            "urllib3==2.2.1   \n" +
            "torch==2.0.1+cu118\n" +
            "empty==\n" +
            "arbitrary===1.0\n" +
            "range>=1.0\n" +
            "-r other.txt\n" +
            "extras[security] == 1.2.3\n";
        const string Expected =
            "dummy1==9.9.9 # pinned\n" +
            "dummy2==9.9.9 ; python_version<'3.8'\n" +
            "dummy3==9.9.9 \\\n" +
            "    --hash=sha256:abc \\\n" +
            "    --hash=sha256:def\n" +
            "dummy4==9.9.9   \n" +
            "dummy5==9.9.9\n" +
            "empty==\n" +
            "arbitrary===1.0\n" +
            "range>=1.0\n" +
            "-r other.txt\n" +
            "dummy6[security] == 9.9.9\n";

        AddFile("requirements.txt", Original);
        var result = await GetDependencies<PythonRequirementsDependencyScanner>();
        Assert.HasCount(6, result);
        AssertContainDependency(result,
            (DependencyType.PyPi, "requests", "2.31.0", 1, 11),
            (DependencyType.PyPi, "pkg", "1.0", 2, 6),
            (DependencyType.PyPi, "certifi", "2024.2.2", 3, 10),
            (DependencyType.PyPi, "urllib3", "2.2.1", 6, 10),
            (DependencyType.PyPi, "torch", "2.0.1+cu118", 7, 8),
            (DependencyType.PyPi, "extras", "1.2.3", 12, 21));

        await UpdateDependencies(result, "dummy", "9.9.9");
        AssertFileContentEqual("requirements.txt", Expected, ignoreNewLines: false);
    }

    [Theory]
    [InlineData("requirements.txt")]
    [InlineData("requirements-dev.txt")]
    [InlineData("dev-requirements.txt")]
    [InlineData("requirements/base.txt")]
    public async Task PythonRequirementsFileNames(string path)
    {
        AddFile(path, "requests==2.31.0\n");

        var result = await GetDependencies<PythonRequirementsDependencyScanner>();

        AssertContainDependency(result, (DependencyType.PyPi, "requests", "2.31.0", 1, 11));
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("requirements.in")]
    [InlineData("requirements/readme.md")]
    [InlineData("requirements/sub/base.txt")]
    public async Task PythonRequirementsFileNames_NotScanned(string path)
    {
        AddFile(path, "requests==2.31.0\n");

        var result = await GetDependencies<PythonRequirementsDependencyScanner>();

        Assert.Empty(result);
    }

    [Fact]
    public async Task DotNetFileBasedAppDependencies()
    {
        const string Original =
            "#!/usr/bin/env dotnet\n" +
            "#:sdk Microsoft.NET.Sdk.Web\n" +
            "#:sdk Aspire.AppHost.Sdk@13.0.2\n" +
            "#:package Newtonsoft.Json\n" +
            "#:package Serilog@3.1.1\n" +
            "#:package Spectre.Console@*\n" +
            "#:project ../SharedLibrary/SharedLibrary.csproj\n" +
            "#:ref ../SharedLibrary/bin/Debug/net10.0/SharedLibrary.dll\n" +
            "#:property TargetFramework=net10.0\n" +
            "\n" +
            "Console.WriteLine(\"Hello, World!\");\n";

        const string Expected =
            "#!/usr/bin/env dotnet\n" +
            "#:sdk dummy1\n" +
            "#:sdk dummy2@2.0.0\n" +
            "#:package dummy3\n" +
            "#:package dummy4@2.0.0\n" +
            "#:package dummy5@2.0.0\n" +
            "#:project dummy6\n" +
            "#:ref dummy7\n" +
            "#:property TargetFramework=2.0.0\n" +
            "\n" +
            "Console.WriteLine(\"Hello, World!\");\n";

        AddFile("app.cs", Original);
        var result = await GetDependencies<DotNetFileBasedAppDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "Microsoft.NET.Sdk.Web", null, 0, 0),
            (DependencyType.NuGet, "Aspire.AppHost.Sdk", "13.0.2", 3, 26),
            (DependencyType.NuGet, "Newtonsoft.Json", null, 0, 0),
            (DependencyType.NuGet, "Serilog", "3.1.1", 5, 19),
            (DependencyType.NuGet, "Spectre.Console", "*", 6, 27),
            (DependencyType.MSBuildProjectReference, "../SharedLibrary/SharedLibrary.csproj", null, 0, 0),
            (DependencyType.DotNetAssemblyReference, "../SharedLibrary/bin/Debug/net10.0/SharedLibrary.dll", null, 0, 0),
            (DependencyType.DotNetTargetFramework, null, "net10.0", 9, 28));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("app.cs", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task DotNetFileBasedAppDependencies_WithComments()
    {
        const string Original =
            "// This is a file-based app\n" +
            "#:package Serilog@3.1.1\n" +
            "\n" +
            "Console.WriteLine(\"Hello\");\n";

        const string Expected =
            "// This is a file-based app\n" +
            "#:package dummy1@2.0.0\n" +
            "\n" +
            "Console.WriteLine(\"Hello\");\n";

        AddFile("app.cs", Original);
        var result = await GetDependencies<DotNetFileBasedAppDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "Serilog", "3.1.1", 2, 19));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("app.cs", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task DotNetFileBasedAppDependencies_WithMultilineComments()
    {
        const string Original = """
            /*
            This is a file-based app
            */
            #:package Serilog@3.1.1

            Console.WriteLine("Hello");
            """;

        const string Expected = """
            /*
            This is a file-based app
            */
            #:package dummy1@2.0.0

            Console.WriteLine("Hello");
            """;

        AddFile("app.cs", Original);
        var result = await GetDependencies<DotNetFileBasedAppDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "Serilog", "3.1.1", 4, 19));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("app.cs", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task DotNetFileBasedAppDependencies_IndentedDirectives()
    {
        const string Original =
            "  #:package Serilog@3.1.1\n" +
            "\t#:sdk Aspire.AppHost.Sdk@13.0.2\n" +
            "   #:property TargetFramework=net10.0\n" +
            "\n" +
            "Console.WriteLine(\"Hello\");\n";

        const string Expected =
            "  #:package dummy1@2.0.0\n" +
            "\t#:sdk dummy2@2.0.0\n" +
            "   #:property TargetFramework=2.0.0\n" +
            "\n" +
            "Console.WriteLine(\"Hello\");\n";

        AddFile("app.cs", Original);
        var result = await GetDependencies<DotNetFileBasedAppDependencyScanner>();
        Assert.HasCount(3, result);
        AssertContainDependency(result,
            (DependencyType.NuGet, "Serilog", "3.1.1", 1, 21),
            (DependencyType.NuGet, "Aspire.AppHost.Sdk", "13.0.2", 2, 27),
            (DependencyType.DotNetTargetFramework, null, "net10.0", 3, 31));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("app.cs", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task DotNetFileBasedAppDependencies_DirectiveAfterCommentOnSameLineIsIgnored()
    {
        // The compiler only accepts a directive as the first non-whitespace text of a line, and stops reading directives after it
        AddFile("app1.cs",
            "/* comment */ #:package Serilog@3.1.1\n" +
            "#:package Newtonsoft.Json@13.0.1\n" +
            "Console.WriteLine(\"Hello\");\n");
        AddFile("app2.cs",
            "/* multi-line\n" +
            "   comment */ #:package Serilog@3.1.1\n" +
            "#:package Newtonsoft.Json@13.0.1\n" +
            "Console.WriteLine(\"Hello\");\n");

        var result = await GetDependencies<DotNetFileBasedAppDependencyScanner>();

        Assert.Empty(result);
    }

    [Fact]
    public async Task DotNetFileBasedAppDependencies_PathsWithSpacesAndPropertyNameCase()
    {
        const string Original =
            "#:project ../My Library/MyLibrary.csproj  \n" +
            "#:ref ../My Library/bin/MyLibrary.dll\n" +
            "#:property targetframework = net10.0\n" +
            "\n" +
            "Console.WriteLine(\"Hello\");\n";

        const string Expected =
            "#:project dummy1  \n" +
            "#:ref dummy2\n" +
            "#:property targetframework = 2.0.0\n" +
            "\n" +
            "Console.WriteLine(\"Hello\");\n";

        AddFile("app.cs", Original);
        var result = await GetDependencies<DotNetFileBasedAppDependencyScanner>();
        Assert.HasCount(3, result);
        AssertContainDependency(result,
            (DependencyType.MSBuildProjectReference, "../My Library/MyLibrary.csproj", null, 0, 0),
            (DependencyType.DotNetAssemblyReference, "../My Library/bin/MyLibrary.dll", null, 0, 0),
            (DependencyType.DotNetTargetFramework, null, "net10.0", 3, 30));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("app.cs", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task PackagesConfigDependencies()
    {
        const string Original = """
            <?xml version="1.0" encoding="utf-8" ?>
            <packages>
              <package id="A" version="4.2.1" targetFramework="net461" />
            </packages>
            """;

        const string Expected = """
            <?xml version="1.0" encoding="utf-8" ?>
            <packages>
              <package id="dummy1" version="2.0.0" targetFramework="net461" />
            </packages>
            """;

        AddFile("packages.config", Original);
        var result = await GetDependencies<PackagesConfigDependencyScanner>();
        AssertContainDependency(result, (DependencyType.NuGet, "A", "4.2.1", 3, 19));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("packages.config", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task ProjectAssetsDependencies()
    {
        AddFile("obj/project.assets.json", /*lang=json,strict*/ """
            {
              "libraries": {
                "DirectDependency/1.0.0": {
                  "type": "package"
                },
                "TransitiveDependency/2.0.0": {
                  "type": "package"
                },
                "ProjectReference/1.0.0": {
                  "type": "project"
                },
                "Invalid": {
                  "type": "package"
                }
              }
            }
            """);

        var result = await GetDependencies<ProjectAssetsDependencyScanner>([new ProjectAssetsDependencyScanner()]);

        AssertContainDependency(result,
            (DependencyType.NuGet, "DirectDependency", "1.0.0", 0, 0),
            (DependencyType.NuGet, "TransitiveDependency", "2.0.0", 0, 0));
        Assert.HasCount(2, result);
        Assert.All(result, dependency =>
        {
            Assert.False(dependency.NameLocation!.IsUpdatable);
            Assert.False(dependency.VersionLocation!.IsUpdatable);
        });
    }

    [Fact]
    public async Task PackagesConfigWithCsprojDependencies()
    {
        const string Original = """
            <?xml version="1.0" encoding="utf-8" ?>
            <packages>
              <package id="NUnit" version="3.11.0" targetFramework="net461" />
            </packages>
            """;

        const string OriginalCsproj = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="..\packages\NUnit.3.11.0\build\NUnit.props" Condition="Exists('..\packages\NUnit.3.11.0\build\NUnit.props')" />
              <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
              <ItemGroup>
                <Reference Include="nunit.framework, Version=3.11.0.0, Culture=neutral, PublicKeyToken=2638cd05610744eb, processorArchitecture=MSIL">
                  <HintPath>..\packages\NUnit.3.11.0\lib\net45\nunit.framework.dll</HintPath>
                </Reference>
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
              <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
                <PropertyGroup>
                  <ErrorText>This project references NuGet package(s) that are missing on this computer. Use NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105. The missing file is {0}.</ErrorText>
                </PropertyGroup>
                <Error Condition="!Exists('..\packages\NUnit.3.11.0\build\NUnit.props')" Text="$([System.String]::Format('$(ErrorText)', '..\packages\NUnit.3.11.0\build\NUnit.props'))" />
              </Target>
            </Project>
            """;

        const string Expected = """
            <?xml version="1.0" encoding="utf-8" ?>
            <packages>
              <package id="dummy1" version="3.12.0-beta00" targetFramework="net461" />
            </packages>
            """;

        const string ExpectedCsproj = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="..\packages\NUnit.3.12.0-beta00\build\NUnit.props" Condition="Exists('..\packages\NUnit.3.12.0-beta00\build\NUnit.props')" />
              <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
              <ItemGroup>
                <Reference Include="nunit.framework, Version=3.12.0.0, Culture=neutral, PublicKeyToken=2638cd05610744eb, processorArchitecture=MSIL">
                  <HintPath>..\packages\NUnit.3.12.0-beta00\lib\net45\nunit.framework.dll</HintPath>
                </Reference>
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
              <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
                <PropertyGroup>
                  <ErrorText>This project references NuGet package(s) that are missing on this computer. Use NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105. The missing file is {0}.</ErrorText>
                </PropertyGroup>
                <Error Condition="!Exists('..\packages\NUnit.3.12.0-beta00\build\NUnit.props')" Text="$([System.String]::Format('$(ErrorText)', '..\packages\NUnit.3.12.0-beta00\build\NUnit.props'))" />
              </Target>
            </Project>
            """;

        AddFile("packages.config", Original);
        AddFile("file.csproj", OriginalCsproj);
        var result = await GetDependencies<PackagesConfigDependencyScanner>();
        await UpdateDependencies(result, "dummy", "3.12.0-beta00");
        AssertFileContentEqual("packages.config", Expected, ignoreNewLines: true);
        AssertFileContentEqual("file.csproj", ExpectedCsproj, ignoreNewLines: true);
    }

    [Theory]
    [InlineData("file.vbproj")]
    [InlineData("file.fsproj")]
    public async Task PackagesConfigWithVbprojOrFsprojDependencies(string projectFileName)
    {
        AddFile("packages.config", """
            <?xml version="1.0" encoding="utf-8" ?>
            <packages>
              <package id="NUnit" version="3.11.0" targetFramework="net461" />
            </packages>
            """);
        AddFile(projectFileName, """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <Reference Include="nunit.framework, Version=3.11.0.0, Culture=neutral">
                  <HintPath>..\packages\NUnit.3.11.0\lib\net45\nunit.framework.dll</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);

        var result = await GetDependencies<PackagesConfigDependencyScanner>();
        await UpdateDependencies(result, "dummy", "3.12.0");

        AssertFileContentEqual(projectFileName, """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <Reference Include="nunit.framework, Version=3.12.0.0, Culture=neutral">
                  <HintPath>..\packages\NUnit.3.12.0\lib\net45\nunit.framework.dll</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """, ignoreNewLines: true);
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public async Task PackagesConfigWithUnreadableProjectFile()
    {
        AddFile("packages.config", """
            <?xml version="1.0" encoding="utf-8" ?>
            <packages>
              <package id="A" version="4.2.1" targetFramework="net461" />
            </packages>
            """);
        File.CreateSymbolicLink(_directory.GetFullPath("dangling.csproj"), _directory.GetFullPath("missing.csproj"));

        var result = await GetDependencies<PackagesConfigDependencyScanner>([new PackagesConfigDependencyScanner()]);

        AssertContainDependency(result, (DependencyType.NuGet, "A", "4.2.1", 3, 19));
    }

    [Fact]
    public async Task PackagesConfigInvalidXml()
    {
        AddFile("packages.config", """<packages><package id="a" version="1.0.0">""");

        var result = await GetDependencies<PackagesConfigDependencyScanner>();

        Assert.Empty(result);
    }

    [Fact]
    public async Task PackagesConfigInvalidXmlDoesNotStopTheScan()
    {
        AddFile("packages.config", """<packages><package id="a" version="1.0.0">""");
        AddFile("Chart.yaml", """
            dependencies:
              - name: mariadb
                version: 7.x.x
                repository: https://example.com/charts
            """);

        var result = await GetDependencies<HelmChartDependencyScanner>();

        AssertContainDependency(result, (DependencyType.HelmChart, "mariadb", "7.x.x", 0, 0));
    }

    [Fact]
    public async Task DockerfileFromDependencies()
    {
        const string Original = """
            FROM a.com/b:1.2.2
            FROM a.com/c:1.2.3 AS base
            COPY --from a.com/d:4.5.6 /tool /tool
            COPY --chown=app:app --from=a.com/e:7.8.9 /src /dest
            COPY --from=base /output /output
            CMD  /code/run-app
            """;
        const string Expected = """
            FROM dummy1:2.0.0
            FROM dummy2:2.0.0 AS base
            COPY --from dummy3:2.0.0 /tool /tool
            COPY --chown=app:app --from=dummy4:2.0.0 /src /dest
            COPY --from=base /output /output
            CMD  /code/run-app
            """;

        AddFile("Dockerfile", Original);
        var result = await GetDependencies<DockerfileDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.DockerImage, "a.com/b", "1.2.2", 1, 14),
            (DependencyType.DockerImage, "a.com/c", "1.2.3", 2, 14),
            (DependencyType.DockerImage, "a.com/d", "4.5.6", 3, 21),
            (DependencyType.DockerImage, "a.com/e", "7.8.9", 4, 37));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Dockerfile", Expected, ignoreNewLines: false);
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\r\r\n")]
    public async Task DockerfileFromDependencies_CarriageReturnLineEndings(string newLine)
    {
        var original = "FROM a.com/b:1.2.2" + newLine + "FROM a.com/c:1.2.3" + newLine + "CMD /code/run-app" + newLine;
        var expected = "FROM dummy1:2.0.0" + newLine + "FROM dummy2:2.0.0" + newLine + "CMD /code/run-app" + newLine;

        AddFile("Dockerfile", original);
        var result = await GetDependencies<DockerfileDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.DockerImage, "a.com/b", "1.2.2", 1, 14),
            (DependencyType.DockerImage, "a.com/c", "1.2.3", newLine == "\r" ? 2 : 3, 14));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Dockerfile", expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task DockerfileFromDependencies_FileThatIsNotValidUtf8_IsScannedButNotCorruptedByUpdates()
    {
        byte[] original = [.. "# caf"u8, 0xE9, .. "\nFROM a.com/b:1.2.2\n"u8];
        AddFile("Dockerfile", original);

        var result = await GetDependencies<DockerfileDependencyScanner>();
        var dependency = Assert.Single(result);
        Assert.Equal("1.2.2", dependency.Version);

        await Assert.ThrowsAsync<DependencyScannerException>(() => dependency.UpdateVersionAsync("2.0.0"));
        Assert.Equal(original, File.ReadAllBytes(_directory.GetFullPath("Dockerfile")));
    }

    [Theory]
    [InlineData("Dockerfile")]
    [InlineData("dockerfile")]
    [InlineData("Dockerfile.prod")]
    [InlineData("app.Dockerfile")]
    [InlineData("Containerfile")]
    [InlineData("Containerfile.prod")]
    [InlineData("app.Containerfile")]
    public async Task DockerfileFileNames(string fileName)
    {
        AddFile(fileName, "FROM a.com/b:1.2.2\n");

        var result = await GetDependencies<DockerfileDependencyScanner>();

        AssertContainDependency(result, (DependencyType.DockerImage, "a.com/b", "1.2.2", 1, 14));
    }

    [Theory]
    [InlineData("NotADockerfile")]
    [InlineData("readme.txt")]
    public async Task DockerfileFileNames_NotScanned(string fileName)
    {
        AddFile(fileName, "FROM a.com/b:1.2.2\n");

        var result = await GetDependencies<DockerfileDependencyScanner>();

        Assert.Empty(result);
    }

    [Fact]
    public async Task DockerfileFromDependencies_Variants()
    {
        const string Original = """
            # syntax=docker/dockerfile:1
            FROM node:18 AS build-env
            FROM --platform=linux/amd64 node:19
              FROM node:20
            FROMnode:1
            FROM localhost:5000/img:1.0 as base
            FROM localhost:5000/noversion
            FROM --platform=$BUILDPLATFORM \
                # comment inside the instruction
                mcr.microsoft.com/dotnet/sdk:8.0 AS build
            COPY --from=localhost:5000/tool:2.0 /tool /tool
            FROM scratch
            FROM node:21 AS build extra
            """;
        const string Expected = """
            # syntax=docker/dockerfile:1
            FROM dummy1:2.0.0 AS build-env
            FROM --platform=linux/amd64 dummy2:2.0.0
              FROM dummy3:2.0.0
            FROMnode:1
            FROM dummy4:2.0.0 as base
            FROM localhost:5000/noversion
            FROM --platform=$BUILDPLATFORM \
                # comment inside the instruction
                dummy5:2.0.0 AS build
            COPY --from=dummy6:2.0.0 /tool /tool
            FROM scratch
            FROM node:21 AS build extra
            """;

        AddFile("Dockerfile", Original);
        var result = await GetDependencies<DockerfileDependencyScanner>();
        Assert.HasCount(6, result);
        AssertContainDependency(result,
            (DependencyType.DockerImage, "node", "18", 2, 11),
            (DependencyType.DockerImage, "node", "19", 3, 34),
            (DependencyType.DockerImage, "node", "20", 4, 13),
            (DependencyType.DockerImage, "localhost:5000/img", "1.0", 6, 25),
            (DependencyType.DockerImage, "mcr.microsoft.com/dotnet/sdk", "8.0", 10, 34),
            (DependencyType.DockerImage, "localhost:5000/tool", "2.0", 11, 33));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Dockerfile", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task DockerfileFromDependencies_DigestsAndVariables()
    {
        AddFile("Dockerfile", """
            FROM node@sha256:abcdef
            FROM node:18@sha256:123456
            FROM node:${NODE_VERSION}
            FROM ${REGISTRY}/node:18
            FROM $IMAGE
            """);

        var result = await GetDependencies<DockerfileDependencyScanner>();
        Assert.HasCount(4, result);

        var digest = Assert.Single(result, d => d.Version == "sha256:abcdef");
        Assert.Equal("node", digest.Name);
        Assert.True(digest.NameLocation!.IsUpdatable);
        Assert.False(digest.VersionLocation!.IsUpdatable);
        Assert.Equal("sha256:abcdef", digest.Metadata["digest"]);
        Assert.Null(digest.Metadata["tag"]);

        var tagAndDigest = Assert.Single(result, d => d.Version == "sha256:123456");
        Assert.Equal("node", tagAndDigest.Name);
        Assert.False(tagAndDigest.VersionLocation!.IsUpdatable);
        Assert.Equal("18", tagAndDigest.Metadata["tag"]);

        var versionVariable = Assert.Single(result, d => d.Version == "${NODE_VERSION}");
        Assert.Equal("node", versionVariable.Name);
        Assert.True(versionVariable.NameLocation!.IsUpdatable);
        Assert.False(versionVariable.VersionLocation!.IsUpdatable);

        var nameVariable = Assert.Single(result, d => d.Name == "${REGISTRY}/node");
        Assert.Equal("18", nameVariable.Version);
        Assert.False(nameVariable.NameLocation!.IsUpdatable);
        Assert.True(nameVariable.VersionLocation!.IsUpdatable);
    }

    [Fact]
    public async Task DockerfileFromDependencies_EscapeDirective()
    {
        AddFile("Dockerfile", """
            # escape=`
            FROM mcr.microsoft.com/windows/servercore:ltsc2022 `
                AS build
            COPY --from=build C:\app C:\app
            """);

        var result = await GetDependencies<DockerfileDependencyScanner>();

        Assert.HasCount(1, result);
        AssertContainDependency(result, (DependencyType.DockerImage, "mcr.microsoft.com/windows/servercore", "ltsc2022", 2, 43));
    }

    [Fact]
    public async Task DockerfileFromDependencies_LongLine()
    {
        AddFile("Dockerfile", "FROM " + string.Concat(Enumerable.Repeat("a:", 32 * 1024)) + " b\nFROM node:18\n");

        var result = await GetDependencies<DockerfileDependencyScanner>();

        Assert.HasCount(1, result);
        AssertContainDependency(result, (DependencyType.DockerImage, "node", "18", 2, 11));
    }

    [Fact]
    public async Task GlobalJsonFromDependencies()
    {
        const string Original = /*lang=json,strict*/ """
{
  "sdk": {
    "version": "3.1.100",
    "rollForward": "disable"
  },
  "msbuild-sdks": {
    "My.Custom.Sdk" : "5.0.0",
    "My.Other.Sdk": "1.0.0-beta"
  }
}
""";
        const string Expected = /*lang=json,strict*/ """
{
  "sdk": {
    "version": "3.1.400",
    "rollForward": "disable"
  },
  "msbuild-sdks": {
    "My.Custom.Sdk" : "3.1.400",
    "My.Other.Sdk": "3.1.400"
  }
}
""";

        AddFile("global.json", Original);
        var result = await GetDependencies<DotNetGlobalJsonDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.DotNetSdk, Name: null, "3.1.100", 0, 0),
            (DependencyType.NuGet, "My.Custom.Sdk", "5.0.0", 0, 0),
            (DependencyType.NuGet, "My.Other.Sdk", "1.0.0-beta", 0, 0));

        await UpdateDependencies(result, "dummy", "3.1.400");
        AssertFileContentEqual("global.json", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task GlobalJsonDependencies_DuplicateKey_IsSkipped()
    {
        AddFile("global.json", """{"sdk":{"version":"8.0.100","version":"9.0.100"}}""");

        var result = await GetDependencies<DotNetGlobalJsonDependencyScanner>();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GitSubmodulesFromDependencies()
    {
        // Initialize remote repository
        await using var remote = TemporaryDirectory.Create();
        await ExecuteProcess("git", "init", remote.FullPath);
        await ExecuteProcess("git", "config user.name test", remote.FullPath);
        await ExecuteProcess("git", "config user.email test@example.com", remote.FullPath);
        await ExecuteProcess("git", "config commit.gpgsign false", remote.FullPath);
        await File.WriteAllTextAsync(remote.GetFullPath("test.txt"), "content");
        await ExecuteProcess("git", "add .", remote.FullPath);
        await ExecuteProcess("git", "commit -m commit-message", remote.FullPath);

        var head = (await ExecuteProcess("git", "rev-parse HEAD", remote.FullPath)).Trim();
        testOutputHelper.WriteLine("Head: " + head);

        // Initialize current directory
        await ExecuteProcess("git", "init", _directory.FullPath);
        await ExecuteProcess("git", "config user.name test", _directory.FullPath);
        await ExecuteProcess("git", "config user.email test@example.com", _directory.FullPath);
        await ExecuteProcess("git", "config commit.gpgsign false", _directory.FullPath);
        await File.WriteAllTextAsync(_directory.GetFullPath("test.txt"), "content");
        await ExecuteProcess("git", "add .", _directory.FullPath);
        await ExecuteProcess("git", "commit -m commit-message", _directory.FullPath);

        // Add submodule
        await ExecuteProcess2("git", ["-c", "protocol.file.allow=always", "submodule", "add", remote.FullPath, "submodule_path"], _directory.FullPath);

        // List files
        var files = Directory.GetFiles(_directory.FullPath, "*", SearchOption.AllDirectories);
        testOutputHelper.WriteLine("Content of " + _directory.FullPath);
        foreach (var file in files)
        {
            var attr = File.GetAttributes(file);
            testOutputHelper.WriteLine($"{file} ({attr})");
        }

        // Assert
        var result = await GetDependencies<GitSubmoduleDependencyScanner>();
        var dependency = Assert.Single(result, d =>
            d.Type == DependencyType.GitReference &&
            d.Version == head &&
            IsEquivalentPath(d.Name, remote.FullPath));

        Assert.False(dependency.NameLocation!.IsUpdatable);
        Assert.True(dependency.VersionLocation!.IsUpdatable);

        await File.WriteAllTextAsync(remote.GetFullPath("test.txt"), "updated-content");
        await ExecuteProcess("git", "add .", remote.FullPath);
        await ExecuteProcess("git", "commit -m updated-submodule-head", remote.FullPath);
        var updatedHead = (await ExecuteProcess("git", "rev-parse HEAD", remote.FullPath)).Trim();

        await dependency.UpdateVersionAsync(updatedHead);

        var submoduleHead = (await ExecuteProcess("git", "-C submodule_path rev-parse HEAD", _directory.FullPath)).Trim();
        Assert.Equal(updatedHead, submoduleHead);

        var indexHead = (await ExecuteProcess("git", "rev-parse :submodule_path", _directory.FullPath)).Trim();
        Assert.Equal(updatedHead, indexHead);

        async Task<string> ExecuteProcess(string process, string args, string workingDirectory)
        {
            testOutputHelper.WriteLine($"Executing: '{process}' {args} ({workingDirectory})");
            var processInstance = ProcessWrapper.Create(process)
                .WithArguments(args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .WithWorkingDirectory(workingDirectory)
                .WithValidation(ProcessValidationMode.None)
                .ExecuteBufferedAsync(XunitCancellationToken);

            var processResult = await processInstance;
            AssertProcessResult(processResult.ExitCode, string.Join('\n', processResult.Output));
            return string.Join('\n', processResult.Output);
        }

        async Task ExecuteProcess2(string process, string[] args, string workingDirectory)
        {
            testOutputHelper.WriteLine($"Executing: '{process}' {string.Join(' ', args)} ({workingDirectory})");
            var processInstance = ProcessWrapper.Create(process)
                .WithArguments(args)
                .WithWorkingDirectory(workingDirectory)
                .WithValidation(ProcessValidationMode.None)
                .ExecuteBufferedAsync(XunitCancellationToken);

            var processResult = await processInstance;
            AssertProcessResult(processResult.ExitCode, string.Join('\n', processResult.Output));
        }

        void AssertProcessResult(int exitCode, string output)
        {
            Assert.Equal(0, exitCode);
            testOutputHelper.WriteLine("git command succeeds\n" + output);
        }

        static bool IsEquivalentPath(string? left, string? right)
        {
            if (left is null || right is null)
                return left is null && right is null;

            var normalizedLeft = left.Replace('\\', '/').TrimEnd('/');
            var normalizedRight = right.Replace('\\', '/').TrimEnd('/');
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GitSubmodulesFromDependenciesInWorktree()
    {
        await using var remote = TemporaryDirectory.Create();
        await ExecuteProcess("git", ["init"], remote.FullPath);
        await ExecuteProcess("git", ["config", "user.name", "test"], remote.FullPath);
        await ExecuteProcess("git", ["config", "user.email", "test@example.com"], remote.FullPath);
        await ExecuteProcess("git", ["config", "commit.gpgsign", "false"], remote.FullPath);
        await File.WriteAllTextAsync(remote.GetFullPath("test.txt"), "content");
        await ExecuteProcess("git", ["add", "."], remote.FullPath);
        await ExecuteProcess("git", ["commit", "-m", "commit-message"], remote.FullPath);

        var head = (await ExecuteProcess("git", ["rev-parse", "HEAD"], remote.FullPath)).Trim();
        testOutputHelper.WriteLine("Head: " + head);

        await ExecuteProcess("git", ["init"], _directory.FullPath);
        await ExecuteProcess("git", ["config", "user.name", "test"], _directory.FullPath);
        await ExecuteProcess("git", ["config", "user.email", "test@example.com"], _directory.FullPath);
        await ExecuteProcess("git", ["config", "commit.gpgsign", "false"], _directory.FullPath);
        await File.WriteAllTextAsync(_directory.GetFullPath("test.txt"), "content");
        await ExecuteProcess("git", ["add", "."], _directory.FullPath);
        await ExecuteProcess("git", ["commit", "-m", "commit-message"], _directory.FullPath);

        await ExecuteProcess("git", ["-c", "protocol.file.allow=always", "submodule", "add", remote.FullPath, "submodule_path"], _directory.FullPath);
        await ExecuteProcess("git", ["add", ".gitmodules", "submodule_path"], _directory.FullPath);
        await ExecuteProcess("git", ["commit", "-m", "add-submodule"], _directory.FullPath);

        await using var worktree = TemporaryDirectory.Create();
        await ExecuteProcess("git", ["worktree", "add", "-b", "scanner-test-worktree", worktree.FullPath], _directory.FullPath);

        var dotGitPath = Path.Combine(worktree.FullPath, ".git");
        Assert.True(File.Exists(dotGitPath));
        Assert.False(Directory.Exists(dotGitPath));

        var options = new ScannerOptions
        {
            DegreeOfParallelism = 1,
            Scanners = [new GitSubmoduleDependencyScanner()],
        };

        var result = await DependencyScanner.ScanDirectoryAsync(worktree.FullPath, options);
        Assert.Contains(result, d =>
            d.Type == DependencyType.GitReference &&
            d.Version == head &&
            IsEquivalentPath(d.Name, remote.FullPath));

        async Task<string> ExecuteProcess(string process, string[] args, string workingDirectory)
        {
            testOutputHelper.WriteLine($"Executing: '{process}' {string.Join(' ', args)} ({workingDirectory})");
            var processInstance = ProcessWrapper.Create(process)
                .WithArguments(args)
                .WithWorkingDirectory(workingDirectory)
                .WithValidation(ProcessValidationMode.None)
                .ExecuteBufferedAsync(XunitCancellationToken);

            var processResult = await processInstance;
            AssertProcessResult(processResult.ExitCode, string.Join('\n', processResult.Output));
            return string.Join('\n', processResult.Output);
        }

        void AssertProcessResult(int exitCode, string output)
        {
            Assert.Equal(0, exitCode);
            testOutputHelper.WriteLine("git command succeeds\n" + output);
        }

        static bool IsEquivalentPath(string? left, string? right)
        {
            if (left is null || right is null)
                return left is null && right is null;

            var normalizedLeft = left.Replace('\\', '/').TrimEnd('/');
            var normalizedRight = right.Replace('\\', '/').TrimEnd('/');
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("sha1", 2, false)]
    [InlineData("sha1", 3, false)]
    [InlineData("sha1", 4, false)]
    [InlineData("sha256", 2, false)]
    [InlineData("sha256", 4, false)]
    [InlineData("sha1", 2, true)]
    [InlineData("sha1", 4, true)]
    [InlineData("sha256", 3, true)]
    public async Task GitSubmodulesFromDependencies_IndexFormats(string objectFormat, int indexVersion, bool splitIndex)
    {
        await RunGitAsync(_directory.FullPath, "init", "--object-format=" + objectFormat);
        await RunGitAsync(_directory.FullPath, "config", "index.version", indexVersion is 4 ? "4" : "2");
        if (splitIndex)
        {
            await RunGitAsync(_directory.FullPath, "config", "core.splitIndex", "true");
            await RunGitAsync(_directory.FullPath, "config", "splitIndex.maxPercentChange", "100");
        }

        var hashLength = objectFormat is "sha256" ? 64 : 40;
        AddFile("file.txt", "content");
        AddFile("libs/readme.md", "content");
        await RunGitAsync(_directory.FullPath, "add", "file.txt", "libs/readme.md");

        // Paths sharing prefixes exercise the path compression of index v4
        string[] submodulePaths = ["libs/alpha", "libs/alpha-beta", "libs/alphabet/nested", "libs/b", "vendor/zeta"];
        foreach (var submodulePath in submodulePaths)
        {
            await AddGitLinkAsync(submodulePath, CreateFakeCommitSha(submodulePath, hashLength));
        }

        if (indexVersion is 3 or 4)
        {
            // Intent-to-add entries use the extended flags, which upgrade a version 2 index to version 3
            AddFile("libs/intent-to-add.txt", "content");
            await RunGitAsync(_directory.FullPath, "add", "--intent-to-add", "libs/intent-to-add.txt");
        }

        if (splitIndex)
        {
            await RunGitAsync(_directory.FullPath, "update-index", "--split-index");

            // Changes after the split are stored in the split index as replacements, deletions and additions
            await AddGitLinkAsync("libs/alpha", CreateFakeCommitSha("libs/alpha-updated", hashLength));
            await RunGitAsync(_directory.FullPath, "update-index", "--force-remove", "libs/b");
            await AddGitLinkAsync("libs/gamma", CreateFakeCommitSha("libs/gamma", hashLength));
        }

        var indexContent = await File.ReadAllBytesAsync(_directory.GetFullPath(".git/index"), XunitCancellationToken);
        var actualIndexVersion = GetIndexVersion(indexContent);
        if (splitIndex)
        {
            Assert.True(indexContent.AsSpan().IndexOf("link"u8) >= 0);
            var sharedIndexContent = await File.ReadAllBytesAsync(GetSharedIndexPath(_directory.GetFullPath(".git"), indexContent), XunitCancellationToken);
            actualIndexVersion = Math.Max(actualIndexVersion, GetIndexVersion(sharedIndexContent));
        }

        Assert.Equal(indexVersion, actualIndexVersion);

        // git is the reference
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (await RunGitAsync(_directory.FullPath, "ls-files", "--stage")).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            var fields = parts[0].Split(' ');
            if (fields[0] == "160000")
            {
                expected.Add(parts[1], fields[1]);
            }
        }

        Assert.HasCount(5, expected);
        AddFile(".gitmodules", string.Concat(expected.Keys.Select(path => $"[submodule \"{path}\"]\n\tpath = {path}\n\turl = https://example.com/{path}.git\n")));

        var result = await GetDependencies<GitSubmoduleDependencyScanner>();
        Assert.Equal(
            expected.Select(item => $"https://example.com/{item.Key}.git@{item.Value}").Order(StringComparer.Ordinal),
            result.Select(d => $"{d.Name}@{d.Version}").Order(StringComparer.Ordinal));

        Task AddGitLinkAsync(string path, string sha) => RunGitAsync(_directory.FullPath, "update-index", "--add", "--cacheinfo", $"160000,{sha},{path}");
    }

    [Fact]
    public async Task GitSubmodulesFromDependencies_GitModulesComments()
    {
        await RunGitAsync(_directory.FullPath, "init");
        string[] submodulePaths = ["libs/a", "libs/b#1", "libs/c", "libs/d"];
        foreach (var submodulePath in submodulePaths)
        {
            await RunGitAsync(_directory.FullPath, "update-index", "--add", "--cacheinfo", $"160000,{CreateFakeCommitSha(submodulePath, 40)},{submodulePath}");
        }

        AddFile(".gitmodules", """
            [submodule "a"] # note
            	path = libs/a # vendored
            	url = https://example.com/a.git ; note
            [submodule "b"]
            	path = "libs/b#1"
            	url = "https://example.com/b.git;x" # comment
            [submodule "c"]
            	path = libs/c
            [submodule "c"]
            	url = https://example.com/\
            c.git
            [submodule "d"]; comment
            	path = libs/d
            	url = "https://example.com/d \"quoted\" \\ .git"
            """.Replace("libs/d", "libs/d\t", StringComparison.Ordinal) + "   ");

        var result = await GetDependencies<GitSubmoduleDependencyScanner>();
        Assert.Equal(
            [
                $"https://example.com/a.git@{CreateFakeCommitSha("libs/a", 40)}",
                $"https://example.com/b.git;x@{CreateFakeCommitSha("libs/b#1", 40)}",
                $"https://example.com/c.git@{CreateFakeCommitSha("libs/c", 40)}",
                $"https://example.com/d \"quoted\" \\ .git@{CreateFakeCommitSha("libs/d", 40)}",
            ],
            result.Select(d => $"{d.Name}@{d.Version}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GitSubmodulesFromDependencies_ReadsThroughFileSystem()
    {
        const string GitModules = "[submodule \"a\"]\n\tpath = libs/a\n\turl = https://example.com/a.git\n";
        var sha = CreateFakeCommitSha("libs/a", 40);
        await RunGitAsync(_directory.FullPath, "init");
        await RunGitAsync(_directory.FullPath, "update-index", "--add", "--cacheinfo", $"160000,{sha},libs/a");
        AddFile(".gitmodules", GitModules);
        var indexContent = await File.ReadAllBytesAsync(_directory.GetFullPath(".git/index"), XunitCancellationToken);

        // The file system scan finds the submodule on disk
        Assert.Single(await GetDependencies<GitSubmoduleDependencyScanner>());

        // The single-file in-memory scan must not read the repository on disk
        var inMemoryResult = await DependencyScanner.ScanFileAsync(_directory.FullPath, _directory.GetFullPath(".gitmodules"), Encoding.UTF8.GetBytes(GitModules), [new GitSubmoduleDependencyScanner()], XunitCancellationToken);
        Assert.Empty(inMemoryResult);

        // A custom file system is used for the .git directory, the gitdir file of worktrees and their common directory
        var root = Path.Combine(Path.GetTempPath(), "virtual-" + Guid.NewGuid().ToString("N"));
        var fileSystem = new GitInMemoryFileSystem();
        fileSystem.AddFile(Path.Combine(root, "repo", ".gitmodules"), GitModules);
        fileSystem.AddFile(Path.Combine(root, "repo", ".git", "index"), indexContent);
        fileSystem.AddFile(Path.Combine(root, "repo", ".git", "config"), "[core]\n\trepositoryformatversion = 0\n");
        fileSystem.AddFile(Path.Combine(root, "worktree", ".gitmodules"), GitModules);
        fileSystem.AddFile(Path.Combine(root, "worktree", ".git"), "gitdir: ../repo/.git/worktrees/wt\n");
        fileSystem.AddFile(Path.Combine(root, "repo", ".git", "worktrees", "wt", "index"), indexContent);
        fileSystem.AddFile(Path.Combine(root, "repo", ".git", "worktrees", "wt", "commondir"), "../..\n");

        var options = new ScannerOptions { FileSystem = fileSystem, Scanners = [new GitSubmoduleDependencyScanner()] };
        foreach (var repository in new[] { "repo", "worktree" })
        {
            var result = await DependencyScanner.ScanFileAsync(root, Path.Combine(root, repository, ".gitmodules"), options, XunitCancellationToken);
            var dependency = Assert.Single(result);
            Assert.Equal("https://example.com/a.git", dependency.Name);
            Assert.Equal(sha, dependency.Version);
        }

        // An unknown object format cannot be read
        fileSystem.AddFile(Path.Combine(root, "repo", ".git", "config"), "[extensions]\n\tobjectFormat = sha512\n");
        Assert.Empty(await DependencyScanner.ScanFileAsync(root, Path.Combine(root, "repo", ".gitmodules"), options, XunitCancellationToken));
    }

    [Fact]
    public async Task GitSubmodulesFromDependencies_MalformedRepositoryIsSkipped()
    {
        const string GitModules = "[submodule \"a\"]\n\tpath = libs/a\n\turl = https://example.com/a.git\n[submodule \"b\"]\n\tpath = libs/abc\n\turl = https://example.com/b.git\n";

        // A .git file with an invalid gitdir path
        AddFile(".gitmodules", GitModules);
        AddFile(".git", "gitdir: invalid\0path\n");
        Assert.Empty(await GetDependencies<GitSubmoduleDependencyScanner>());

        // A .git directory without an index: opening the directory as a file must not throw
        File.Delete(_directory.GetFullPath(".git"));
        await RunGitAsync(_directory.FullPath, "init");
        Assert.Empty(await GetDependencies<GitSubmoduleDependencyScanner>());

        // Truncated and corrupted indexes, in every supported format
        var root = Path.Combine(Path.GetTempPath(), "virtual-" + Guid.NewGuid().ToString("N"));
        var gitModulesPath = Path.Combine(root, ".gitmodules");
        var indexPath = Path.Combine(root, ".git", "index");
        var validIndexes = new List<(byte[] Index, string SharedIndexName, byte[] SharedIndex)>();
        foreach (var indexVersion in new[] { "2", "4" })
        {
            await using var repository = TemporaryDirectory.Create();
            await RunGitAsync(repository.FullPath, "init");
            await RunGitAsync(repository.FullPath, "config", "index.version", indexVersion);
            await RunGitAsync(repository.FullPath, "config", "core.splitIndex", "true");
            await RunGitAsync(repository.FullPath, "config", "splitIndex.maxPercentChange", "100");
            await RunGitAsync(repository.FullPath, "update-index", "--add", "--cacheinfo", $"160000,{CreateFakeCommitSha("libs/a", 40)},libs/a");
            await RunGitAsync(repository.FullPath, "update-index", "--add", "--cacheinfo", $"160000,{CreateFakeCommitSha("libs/abc", 40)},libs/abc");
            await RunGitAsync(repository.FullPath, "update-index", "--split-index");
            await RunGitAsync(repository.FullPath, "update-index", "--cacheinfo", $"160000,{CreateFakeCommitSha("libs/a2", 40)},libs/a");
            await RunGitAsync(repository.FullPath, "update-index", "--add", "--cacheinfo", $"160000,{CreateFakeCommitSha("libs/c", 40)},libs/c");

            var index = await File.ReadAllBytesAsync(repository.GetFullPath(".git/index"), XunitCancellationToken);
            var sharedIndexPath = GetSharedIndexPath(repository.GetFullPath(".git"), index);
            validIndexes.Add((index, Path.GetFileName(sharedIndexPath), await File.ReadAllBytesAsync(sharedIndexPath, XunitCancellationToken)));
        }

        foreach (var (index, sharedIndexName, sharedIndex) in validIndexes)
        {
            var candidates = new List<(byte[] Index, byte[] SharedIndex)> { (index, sharedIndex) };
            for (var length = 0; length < index.Length; length++)
            {
                candidates.Add((index[..length], sharedIndex));
            }

            for (var length = 0; length < sharedIndex.Length; length++)
            {
                candidates.Add((index, sharedIndex[..length]));
            }

            // Deterministic byte corruptions of both files
            foreach (var target in new[] { index, sharedIndex })
            {
                for (var position = 0; position < target.Length; position++)
                {
                    foreach (var value in new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF, (byte)(target[position] ^ 0x40) })
                    {
                        var corrupted = target.ToArray();
                        corrupted[position] = value;
                        candidates.Add(target == index ? (corrupted, sharedIndex) : (index, corrupted));
                    }
                }
            }

            foreach (var (candidateIndex, candidateSharedIndex) in candidates)
            {
                var fileSystem = new GitInMemoryFileSystem();
                fileSystem.AddFile(gitModulesPath, GitModules);
                fileSystem.AddFile(indexPath, candidateIndex);
                fileSystem.AddFile(Path.Combine(root, ".git", sharedIndexName), candidateSharedIndex);

                var options = new ScannerOptions { FileSystem = fileSystem, Scanners = [new GitSubmoduleDependencyScanner()] };
                var result = await DependencyScanner.ScanFileAsync(root, gitModulesPath, options, XunitCancellationToken);
                Assert.True(result.Count <= 2);
            }
        }
    }

    private static int GetIndexVersion(byte[] indexContent) => (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(indexContent.AsSpan(4, 4));

    // git does not delete the previous shared indexes right away, so find the one referenced by the link extension
    private static string GetSharedIndexPath(string gitDirectory, byte[] indexContent)
    {
        return Assert.Single(Directory.GetFiles(gitDirectory, "sharedindex.*"), path =>
        {
            var hash = Convert.FromHexString(Path.GetExtension(path).TrimStart('.'));
            return indexContent.AsSpan().IndexOf("link"u8) is var linkIndex and >= 0 && indexContent.AsSpan(linkIndex + 8).StartsWith(hash);
        });
    }

    private static string CreateFakeCommitSha(string seed, int length)
    {
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..length].PadRight(length, 'a');
    }

    private async Task<string> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        testOutputHelper.WriteLine($"Executing: git {string.Join(' ', arguments)} ({workingDirectory})");
        var result = await ProcessWrapper.Create("git")
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory)
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync(XunitCancellationToken);

        var output = string.Join('\n', result.Output.StandardOutput.Select(line => line.Text));
        testOutputHelper.WriteLine(string.Join('\n', result.Output));
        Assert.Equal(0, result.ExitCode);
        return output;
    }

    private sealed class GitInMemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public void AddFile(string path, byte[] content) => _files[path] = content;

        public void AddFile(string path, string content) => _files[path] = Encoding.UTF8.GetBytes(content);

        public Stream OpenRead(string path) => _files.TryGetValue(path, out var content) ? new MemoryStream(content, writable: false) : throw new FileNotFoundException("File not found", path);

        public Stream OpenReadWrite(string path) => throw new NotSupportedException();

        public IEnumerable<string> GetFiles(string path, string pattern, SearchOption searchOptions) => throw new NotSupportedException();
    }

    [Fact]
    public async Task GitHubActions()
    {
        const string Path = ".github/workflows/sample.yml";
        const string Original = """
name: demo
on: [push]
jobs:
    check-bats-version:
        runs-on: ubuntu-latest
        steps:
            - uses: actions/checkout@v2
            - uses: actions/setup-node@v1
            - uses: docker://test/setup:v3
            - uses: "docker://image/without/version"
            - uses: "sample-org/project/.github/workflows/test@main"
            - run: npm install -g bats
            - run: bats -v
        container:
            image: node:10.16-jessie
        services:
            nginx:
                image: nginx:latest
            redis:
                image: redis:1.0
            service3:
                image: alpine
    job_template:
        uses: sample/template.yml@v1
""";
        const string Expected = """
name: demo
on: [push]
jobs:
    check-bats-version:
        runs-on: ubuntu-latest
        steps:
            - uses: dummy1@v3.0.0
            - uses: dummy2@v3.0.0
            - uses: docker://dummy3:v3.0.0
            - uses: "docker://dummy4"
            - uses: "dummy5@v3.0.0"
            - run: npm install -g bats
            - run: bats -v
        container:
            image: dummy6:v3.0.0
        services:
            nginx:
                image: dummy7:v3.0.0
            redis:
                image: dummy8:v3.0.0
            service3:
                image: dummy9
    job_template:
        uses: dummy10@v3.0.0
""";

        AddFile(Path, Original);
        var result = await GetDependencies<GitHubActionsScanner>();
        AssertContainDependency(result,
            (DependencyType.GitHubActions, "actions/checkout", "v2", 7, 38),
            (DependencyType.GitHubActions, "actions/setup-node", "v1", 8, 40),
            (DependencyType.DockerImage, "test/setup", "v3", 9, 41),
            (DependencyType.DockerImage, "image/without/version", null, 0, 0),
            (DependencyType.GitHubActions, "sample-org/project/.github/workflows/test", "main", 11, 64),
            (DependencyType.DockerImage, "node", "10.16-jessie", 15, 25),
            (DependencyType.DockerImage, "nginx", "latest", 18, 30),
            (DependencyType.DockerImage, "redis", "1.0", 20, 30),
            (DependencyType.DockerImage, "alpine", null, 0, 0));

        await UpdateDependencies(result, "dummy", "v3.0.0");
        AssertFileContentEqual(Path, Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task GitHubActions_WrongPath_NoDependency()
    {
        const string Path = "sample/.github/workflows/sample.yml";
        const string Original = """
name: demo
on: [push]
jobs:
    check-bats-version:
        runs-on: ubuntu-latest
        steps:
            - uses: actions/checkout@v2
            - uses: actions/setup-node@v1
""";
        AddFile(Path, Original);
        var result = await GetDependencies<GitHubActionsScanner>();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GitHubActions_ImageReferencesAndLocalActions()
    {
        const string Path = ".github/workflows/sample.yml";
        const string Original = """
jobs:
  build:
    runs-on: ubuntu-latest
    container: localhost:5000/node:18
    services:
      db:
        image: postgres@sha256:abcdef
    steps:
      - uses: ./.github/actions/local
      - uses: ../shared/action
      - uses: docker://localhost:5000/tool:2.0
      - uses: actions/checkout@v4
  reusable:
    uses: ./.github/workflows/reusable.yml
""";
        const string Expected = """
jobs:
  build:
    runs-on: ubuntu-latest
    container: dummy1:3.0.0
    services:
      db:
        image: dummy2@sha256:abcdef
    steps:
      - uses: ./.github/actions/local
      - uses: ../shared/action
      - uses: docker://dummy3:3.0.0
      - uses: dummy4@3.0.0
  reusable:
    uses: ./.github/workflows/reusable.yml
""";

        AddFile(Path, Original);
        var result = await GetDependencies<GitHubActionsScanner>();
        Assert.HasCount(4, result);
        AssertContainDependency(result,
            (DependencyType.DockerImage, "localhost:5000/node", "18", 4, 36),
            (DependencyType.DockerImage, "postgres", "sha256:abcdef", 0, 0),
            (DependencyType.DockerImage, "localhost:5000/tool", "2.0", 11, 44),
            (DependencyType.GitHubActions, "actions/checkout", "v4", 12, 32));
        Assert.False(Assert.Single(result, d => d.Name == "postgres").VersionLocation!.IsUpdatable);

        await UpdateDependencies(result, "dummy", "3.0.0");
        AssertFileContentEqual(Path, Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task GitHubActions_ScalarStylesAndAliases()
    {
        const string Path = ".github/workflows/sample.yml";
        const string Original = """
jobs:
  build:
    steps:
      - uses: 'actions/checkout@v1'
      - uses: "actions/setup-node@v2"
      - uses: "actions/setup-dotnet@v\x33"
      - uses: >-
          actions/cache@v4
      - uses: &upload actions/upload-artifact@v5
      - uses: *upload
      - uses: !!str actions/download-artifact@v6
""";
        const string Expected = """
jobs:
  build:
    steps:
      - uses: 'dummy1@v9'
      - uses: "dummy2@v9"
      - uses: "actions/setup-dotnet@v\x33"
      - uses: >-
          actions/cache@v4
      - uses: &upload dummy3@v9
      - uses: *upload
      - uses: !!str dummy4@v9
""";

        AddFile(Path, Original);
        var result = await GetDependencies<GitHubActionsScanner>();
        Assert.HasCount(6, result);
        AssertContainDependency(result,
            (DependencyType.GitHubActions, "actions/checkout", "v1", 4, 33),
            (DependencyType.GitHubActions, "actions/setup-node", "v2", 5, 35),
            (DependencyType.GitHubActions, "actions/setup-dotnet", "v3", 0, 0),
            (DependencyType.GitHubActions, "actions/cache", "v4", 0, 0),
            (DependencyType.GitHubActions, "actions/upload-artifact", "v5", 9, 47),
            (DependencyType.GitHubActions, "actions/download-artifact", "v6", 11, 47));

        var escaped = Assert.Single(result, d => d.Name == "actions/setup-dotnet");
        Assert.False(escaped.NameLocation!.IsUpdatable);
        Assert.False(escaped.VersionLocation!.IsUpdatable);

        var folded = Assert.Single(result, d => d.Name == "actions/cache");
        Assert.False(folded.NameLocation!.IsUpdatable);
        Assert.False(folded.VersionLocation!.IsUpdatable);

        await UpdateDependencies(result, "dummy", "v9");
        AssertFileContentEqual(Path, Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task GitHubActions_ActionMetadataFiles()
    {
        AddFile("actions/composite/action.yml", """
            name: composite
            runs:
              using: composite
              steps:
                - uses: actions/setup-node@v4
                - uses: ./local
            """);
        AddFile("actions/docker/action.yaml", """
            name: docker
            runs:
              using: docker
              image: docker://alpine:3.20
            """);
        AddFile("actions/dockerfile/action.yml", """
            runs:
              using: docker
              image: Dockerfile
            """);

        var result = await GetDependencies<GitHubActionsScanner>();

        Assert.HasCount(2, result);
        AssertContainDependency(result,
            (DependencyType.GitHubActions, "actions/setup-node", "v4", 5, 32),
            (DependencyType.DockerImage, "alpine", "3.20", 4, 26));
    }

    [Fact]
    public async Task GitHubActions_RootDirectoryWithTrailingSeparator()
    {
        AddFile(".github/workflows/sample.yml", """
            jobs:
              build:
                steps:
                  - uses: actions/checkout@v4
            """);

        var rootDirectory = _directory.FullPath.Value + System.IO.Path.DirectorySeparatorChar;
        var filePath = _directory.GetFullPath(".github/workflows/sample.yml");
        var result = await DependencyScanner.ScanFileAsync(rootDirectory, filePath, new ScannerOptions { Scanners = [new GitHubActionsScanner()] }, XunitCancellationToken);

        Assert.Single(result, d => d.Name == "actions/checkout");
    }

    [Fact]
    public async Task Regex()
    {
        const string Original = """
            container:
              image: node:10
            services:
              abc
            """;
        const string Expected = """
            container:
              image: dummy1:v3.0.0
            services:
              abc
            """;

        AddFile("custom/sample.yml", Original);
        var result = await GetDependencies<RegexScanner>([new RegexScanner()
        {
            FilePatterns = new GlobCollection(Glob.Parse("**/*", GlobDialect.Standard)),
            DependencyType = DependencyType.DockerImage,
            Regex = DockerImageWithVersionRegex(),
        }]);
        AssertContainDependency(result,
            (DependencyType.DockerImage, "node", "10", 2, 15));

        await UpdateDependencies(result, "dummy", "v3.0.0");
        AssertFileContentEqual("custom/sample.yml", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task Regex_FirstLine()
    {
        const string Original = """
            image: node:10
            image: alpine:3
            """;
        const string Expected = """
            image: dummy1:2.0.0
            image: dummy2:2.0.0
            """;

        AddFile("custom/sample.yml", Original);
        var result = await GetDependencies<RegexScanner>([new RegexScanner()
        {
            FilePatterns = new GlobCollection(Glob.Parse("**/*", GlobDialect.Standard)),
            DependencyType = DependencyType.DockerImage,
            Regex = DockerImageWithVersionRegex(),
        }]);
        AssertContainDependency(result,
            (DependencyType.DockerImage, "node", "10", 1, 13),
            (DependencyType.DockerImage, "alpine", "3", 2, 15));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("custom/sample.yml", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task Regex_OptionalVersion()
    {
        const string Original = """
            container:
              image: node
            services:
              abc
            """;
        const string Expected = """
            container:
              image: dummy1
            services:
              abc
            """;

        AddFile("custom/sample.yml", Original);
        var result = await GetDependencies<RegexScanner>([new RegexScanner()
        {
            FilePatterns = new GlobCollection(Glob.Parse("**/*", GlobDialect.Standard)),
            DependencyType = DependencyType.DockerImage,
            Regex = DockerImageWithOptionalVersionRegex(),
        }]);
        AssertContainDependency(result,
            (DependencyType.DockerImage, "node", null, 0, 0));

        await UpdateDependencies(result, "dummy", "v3.0.0");
        AssertFileContentEqual("custom/sample.yml", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task Regex_LeavesTheSharedStreamOpenForTheNextScanner()
    {
        AddFile("package.json", /*lang=json,strict*/ """
{
  "dependencies": {
    "a": "1.0.0"
  }
}
""");

        var result = await GetDependencies<NpmPackageJsonDependencyScanner>([
            new RegexScanner
            {
                FilePatterns = new GlobCollection(Glob.Parse("**/*", GlobDialect.Standard)),
                DependencyType = DependencyType.DockerImage,
                Regex = DockerImageWithVersionRegex(),
            },
            new NpmPackageJsonDependencyScanner(),
        ]);

        AssertContainDependency(result, (DependencyType.Npm, "a", "1.0.0", 0, 0));
    }

    [Fact]
    public async Task Regex_FilePatternsAreRelativeToTheRootDirectory()
    {
        AddFile("build/ci.yml", "image: node:10");
        AddFile("other/build/ci.yml", "image: node:11");
        AddFile("root.yml", "image: node:12");

        var relativeResult = await GetDependencies<RegexScanner>([new RegexScanner()
        {
            FilePatterns = new GlobCollection(Glob.Parse("build/*.yml", GlobDialect.Standard)),
            DependencyType = DependencyType.DockerImage,
            Regex = DockerImageWithVersionRegex(),
        }]);
        Assert.HasCount(1, relativeResult);
        AssertContainDependency(relativeResult, (DependencyType.DockerImage, "node", "10", 1, 13));

        var recursiveResult = await GetDependencies<RegexScanner>([new RegexScanner()
        {
            FilePatterns = new GlobCollection(Glob.Parse("**/*.yml", GlobDialect.Standard)),
            DependencyType = DependencyType.DockerImage,
            Regex = DockerImageWithVersionRegex(),
        }]);
        Assert.HasCount(3, recursiveResult);
        AssertContainDependency(recursiveResult,
            (DependencyType.DockerImage, "node", "10", 1, 13),
            (DependencyType.DockerImage, "node", "11", 1, 13),
            (DependencyType.DockerImage, "node", "12", 1, 13));
    }

    [Fact]
    public async Task Regex_LineBreaks()
    {
        AddFile("custom/sample.yml", "a\r\nimage: node:10\rimage: alpine:3\n\nimage: nginx:1");

        var result = await GetDependencies<RegexScanner>([new RegexScanner()
        {
            FilePatterns = new GlobCollection(Glob.Parse("**/*", GlobDialect.Standard)),
            DependencyType = DependencyType.DockerImage,
            Regex = DockerImageWithVersionRegex(),
        }]);

        Assert.HasCount(3, result);
        AssertContainDependency(result,
            (DependencyType.DockerImage, "node", "10", 2, 13),
            (DependencyType.DockerImage, "alpine", "3", 3, 15),
            (DependencyType.DockerImage, "nginx", "1", 5, 14));
        var nginx = Assert.Single(result, d => d.Name == "nginx");
        Assert.Equal(5, ((ILocationLineInfo)nginx.NameLocation!).LineNumber);
        Assert.Equal(8, ((ILocationLineInfo)nginx.NameLocation!).LinePosition);
    }

    [Fact]
    public async Task DotNetToolsDependencies()
    {
        const string Original = /*lang=json,strict*/ """
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-validate": {
      "version": "0.0.1-preview.130",
      "commands": [
        "dotnet-validate"
      ]
    },
    "dotnet-format": {
      "version": "5.0.211103",
      "commands": [
        "dotnet-format"
      ]
    }
  }
}
""";

        const string Expected = /*lang=json,strict*/ """
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-validate": {
      "version": "2.0.0",
      "commands": [
        "dotnet-validate"
      ]
    },
    "dotnet-format": {
      "version": "2.0.0",
      "commands": [
        "dotnet-format"
      ]
    }
  }
}
""";

        AddFile("dotnet-tools.json", Original);
        var result = await GetDependencies<DotNetToolManifestDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "dotnet-validate", "0.0.1-preview.130", 0, 0),
            (DependencyType.NuGet, "dotnet-format", "5.0.211103", 0, 0));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("dotnet-tools.json", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task DotNetToolsDependencies_InvalidUtf8_IsSkipped()
    {
        AddFile("dotnet-tools.json", [.. """{"version":1,"tools":{"dotnet-format":{"version":"5."""u8, 0xFF, .. "\"}}}"u8]);

        var result = await GetDependencies<DotNetToolManifestDependencyScanner>();

        Assert.Empty(result);
    }

    [Fact]
    public async Task SwiftPackageResolvedDependencies()
    {
        const string Original = /*lang=json,strict*/ """
{
  "version": 2,
  "pins": [
    {
      "identity": "swift-nio",
      "kind": "remoteSourceControl",
      "location": "https://github.com/apple/swift-nio.git",
      "state": {
        "revision": "3ef4f4ec7fcd0d949f79e421bd1209e7ee48357f",
        "version": "2.76.0"
      }
    },
    {
      "identity": "swift-log",
      "kind": "remoteSourceControl",
      "location": "https://github.com/apple/swift-log.git",
      "state": {
        "branch": "main",
        "revision": "01f8f2c34d3ebd20e607d06c3ca4d62f6ec4f234"
      }
    },
    {
      "identity": "swift-collections",
      "kind": "remoteSourceControl",
      "location": "https://github.com/apple/swift-collections.git",
      "state": {
        "revision": "671108c96644956dddcd89dd59c203dcdb36cec7"
      }
    }
  ]
}
""";

        const string Expected = /*lang=json,strict*/ """
{
  "version": 2,
  "pins": [
    {
      "identity": "dummy1",
      "kind": "remoteSourceControl",
      "location": "https://github.com/apple/swift-nio.git",
      "state": {
        "revision": "3ef4f4ec7fcd0d949f79e421bd1209e7ee48357f",
        "version": "2.76.0"
      }
    },
    {
      "identity": "dummy2",
      "kind": "remoteSourceControl",
      "location": "https://github.com/apple/swift-log.git",
      "state": {
        "branch": "main",
        "revision": "01f8f2c34d3ebd20e607d06c3ca4d62f6ec4f234"
      }
    },
    {
      "identity": "dummy3",
      "kind": "remoteSourceControl",
      "location": "https://github.com/apple/swift-collections.git",
      "state": {
        "revision": "2.0.0"
      }
    }
  ]
}
""";

        AddFile("Package.resolved", Original);
        var result = await GetDependencies<SwiftPackageDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.SwiftPackage, "swift-nio", "2.76.0", 0, 0),
            (DependencyType.SwiftPackage, "swift-log", "main", 0, 0),
            (DependencyType.SwiftPackage, "swift-collections", "671108c96644956dddcd89dd59c203dcdb36cec7", 0, 0));

        // SwiftPM checks out the pinned revision, so only a revision-only pin can be updated without leaving the pin inconsistent
        Assert.False(Assert.Single(result, d => d.Name == "swift-nio").VersionLocation!.IsUpdatable);
        Assert.False(Assert.Single(result, d => d.Name == "swift-log").VersionLocation!.IsUpdatable);
        Assert.True(Assert.Single(result, d => d.Name == "swift-collections").VersionLocation!.IsUpdatable);

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Package.resolved", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task SwiftPackageResolvedDependencies_V1Schema()
    {
        const string Original = /*lang=json,strict*/ """
{
  "object": {
    "pins": [
      {
        "package": "alamofire",
        "repositoryURL": "https://github.com/alamofire/alamofire.git",
        "state": {
          "branch": null,
          "revision": "1c8560d32e6c41f6ad4c235f5be9c6f2248f5d7c",
          "version": "5.8.0"
        }
      }
    ]
  },
  "version": 1
}
""";

        const string Expected = /*lang=json,strict*/ """
{
  "object": {
    "pins": [
      {
        "package": "dummy1",
        "repositoryURL": "https://github.com/alamofire/alamofire.git",
        "state": {
          "branch": null,
          "revision": "1c8560d32e6c41f6ad4c235f5be9c6f2248f5d7c",
          "version": "5.8.0"
        }
      }
    ]
  },
  "version": 1
}
""";

        AddFile("Package.resolved", Original);
        var result = await GetDependencies<SwiftPackageDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.SwiftPackage, "alamofire", "5.8.0", 0, 0));
        Assert.False(Assert.Single(result).VersionLocation!.IsUpdatable);

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Package.resolved", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task SwiftPackageManifestDependencies()
    {
        const string Original = """
            // .package(url: "https://example.com/ignored.git", from: "0.0.1")
            let package = Package(
                name: "Sample",
                dependencies: [
                    .package(url: "https://github.com/apple/swift-nio.git", from: "2.76.0"),
                    .package(
                        id: "apple/swift-collections",
                        .upToNextMajor(from: "1.1.0")
                    ),
                    .package(path: "../LocalPackage")
                ]
            )
            """;

        const string Expected = """
            // .package(url: "https://example.com/ignored.git", from: "0.0.1")
            let package = Package(
                name: "Sample",
                dependencies: [
                    .package(url: "https://github.com/apple/swift-nio.git", exact: "2.0.0"),
                    .package(
                        id: "apple/swift-collections",
                        .upToNextMajor(from: "1.1.0")
                    ),
                    .package(path: "../LocalPackage")
                ]
            )
            """;

        AddFile("Package.swift", Original);
        var result = await GetDependencies<SwiftPackageDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.SwiftPackage, "https://github.com/apple/swift-nio.git", "from: \"2.76.0\"", 0, 0),
            (DependencyType.SwiftPackage, "apple/swift-collections", ".upToNextMajor(from: \"1.1.0\")", 0, 0),
            (DependencyType.SwiftPackage, "../LocalPackage", null, 0, 0));

        var swiftNioDependency = Assert.Single(result, d => d.Name == "https://github.com/apple/swift-nio.git");
        await swiftNioDependency.UpdateVersionAsync("exact: \"2.0.0\"");
        AssertFileContentEqual("Package.swift", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task SwiftPackageManifestDependencies_RequirementArgumentIsSelectedExplicitly()
    {
        const string Original = """
            let package = Package(
                name: "Sample",
                dependencies: [
                    .package(url: "https://github.com/apple/swift-nio.git", from: "2.76.0", traits: [.defaults]),
                    .package(name: "Legacy", url: "https://example.com/legacy.git", .exact("1.2.3")),
                    .package(url: "https://example.com/range.git", "1.0.0"..<"2.0.0"),
                    .package(url: "https://example.com/closed-range.git", "1.0.0"..."1.5.0"),
                    .package(url: "https://example.com/minor.git", .upToNextMinor(from: "1.1.0")),
                    .package(url: "https://example.com/branch.git", branch: "main"),
                    .package(url: "https://example.com/revision.git", revision: "abc123"),
                    .package(url: "https://example.com/old-branch.git", .branch("develop")),
                    .package(url: "https://example.com/qualified.git", Package.Dependency.Requirement.revision("def456")),
                    .package(id: "scope.registry", exact: "3.0.0", traits: ["Foo"]),
                ]
            )
            """;

        const string Expected = """
            let package = Package(
                name: "Sample",
                dependencies: [
                    .package(url: "https://github.com/apple/swift-nio.git", exact: "2.80.0", traits: [.defaults]),
                    .package(name: "Legacy", url: "https://example.com/legacy.git", .exact("1.2.3")),
                    .package(url: "https://example.com/range.git", "1.0.0"..<"2.0.0"),
                    .package(url: "https://example.com/closed-range.git", "1.0.0"..."1.5.0"),
                    .package(url: "https://example.com/minor.git", .upToNextMinor(from: "1.1.0")),
                    .package(url: "https://example.com/branch.git", branch: "main"),
                    .package(url: "https://example.com/revision.git", revision: "abc123"),
                    .package(url: "https://example.com/old-branch.git", .branch("develop")),
                    .package(url: "https://example.com/qualified.git", Package.Dependency.Requirement.revision("def456")),
                    .package(id: "scope.registry", exact: "3.0.0", traits: ["Foo"]),
                ]
            )
            """;

        AddFile("Package.swift", Original);
        var result = await GetDependencies<SwiftPackageDependencyScanner>();
        Assert.HasCount(10, result);
        AssertContainDependency(result,
            (DependencyType.SwiftPackage, "https://github.com/apple/swift-nio.git", "from: \"2.76.0\"", 4, 65),
            (DependencyType.SwiftPackage, "Legacy", ".exact(\"1.2.3\")", 0, 0),
            (DependencyType.SwiftPackage, "https://example.com/range.git", "\"1.0.0\"..<\"2.0.0\"", 0, 0),
            (DependencyType.SwiftPackage, "https://example.com/closed-range.git", "\"1.0.0\"...\"1.5.0\"", 0, 0),
            (DependencyType.SwiftPackage, "https://example.com/minor.git", ".upToNextMinor(from: \"1.1.0\")", 0, 0),
            (DependencyType.SwiftPackage, "https://example.com/branch.git", "branch: \"main\"", 0, 0),
            (DependencyType.SwiftPackage, "https://example.com/revision.git", "revision: \"abc123\"", 0, 0),
            (DependencyType.SwiftPackage, "https://example.com/old-branch.git", ".branch(\"develop\")", 0, 0),
            (DependencyType.SwiftPackage, "https://example.com/qualified.git", "Package.Dependency.Requirement.revision(\"def456\")", 0, 0),
            (DependencyType.SwiftPackage, "scope.registry", "exact: \"3.0.0\"", 0, 0));

        var swiftNioDependency = Assert.Single(result, d => d.Name == "https://github.com/apple/swift-nio.git");
        await swiftNioDependency.UpdateVersionAsync("exact: \"2.80.0\"");
        AssertFileContentEqual("Package.swift", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task SwiftPackageManifestDependencies_CommentsAreNotPartOfArguments()
    {
        const string Original = """
            let package = Package(
                name: "Sample",
                dependencies: [
                    .package(
                        // networking
                        url: "https://github.com/apple/swift-nio.git",
                        from: "2.76.0" // pinned
                    ),
                    .package(url: /* mirror */ "https://github.com/apple/swift-log.git", /* keep */ exact: "1.5.0" /* reviewed */),
                ]
            )
            """;

        const string Expected = """
            let package = Package(
                name: "Sample",
                dependencies: [
                    .package(
                        // networking
                        url: "https://github.com/apple/swift-nio.git",
                        exact: "2.0.0" // pinned
                    ),
                    .package(url: /* mirror */ "https://github.com/apple/swift-log.git", /* keep */ exact: "2.0.0" /* reviewed */),
                ]
            )
            """;

        AddFile("Package.swift", Original);
        var result = await GetDependencies<SwiftPackageDependencyScanner>();
        Assert.HasCount(2, result);
        AssertContainDependency(result,
            (DependencyType.SwiftPackage, "https://github.com/apple/swift-nio.git", "from: \"2.76.0\"", 7, 13),
            (DependencyType.SwiftPackage, "https://github.com/apple/swift-log.git", "exact: \"1.5.0\"", 9, 89));

        foreach (var dependency in result)
        {
            await dependency.UpdateVersionAsync("exact: \"2.0.0\"");
        }

        AssertFileContentEqual("Package.swift", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task SwiftPackageManifestDependencies_RawMultilineAndInterpolatedStrings()
    {
        const string Original = """"
            let windowsPath = #"C:\"#
            let quote = #"He said "hi""#
            let doc = """
                Use .package(url: "https://example.com/ignored.git", from: "0.0.1") in your manifest
                A lone " quote
                """
            let interpolated = "\(String(")"))"
            /* outer /* nested */ .package(url: "https://example.com/ignored-comment.git", from: "0.0.1") */
            let package = Package(
                name: "Sample",
                dependencies: [
                    .package(url: #"https://example.com/raw.git"#, from: "1.0.0"),
                    .package(url: """
                        https://example.com/multiline.git
                        """, from: "3.0.0"),
                    .package(url: "https://github.com/apple/swift-nio.git", from: "2.76.0"),
                ]
            )
            """";

        AddFile("Package.swift", Original);
        var result = await GetDependencies<SwiftPackageDependencyScanner>();
        Assert.HasCount(3, result);
        AssertContainDependency(result,
            (DependencyType.SwiftPackage, "https://example.com/raw.git", "from: \"1.0.0\"", 12, 56),
            (DependencyType.SwiftPackage, "https://example.com/multiline.git", "from: \"3.0.0\"", 15, 18),
            (DependencyType.SwiftPackage, "https://github.com/apple/swift-nio.git", "from: \"2.76.0\"", 16, 65));

        var rawDependency = Assert.Single(result, d => d.Name == "https://example.com/raw.git");
        Assert.True(rawDependency.NameLocation!.IsUpdatable);

        var multilineDependency = Assert.Single(result, d => d.Name == "https://example.com/multiline.git");
        Assert.False(multilineDependency.NameLocation!.IsUpdatable);
    }

    [Fact]
    public async Task SwiftPackageManifestDependencies_QualifiedPackageCall()
    {
        const string Original = """
            let dependencies: [Package.Dependency] = [
                Package.Dependency.package(url: "https://example.com/qualified.git", from: "1.0.0"),
                Dependency.package(url: "https://example.com/dependency.git", from: "2.0.0"),
                foo.package(url: "https://example.com/ignored.git", from: "3.0.0"),
            ]
            """;

        AddFile("Package.swift", Original);
        var result = await GetDependencies<SwiftPackageDependencyScanner>();
        Assert.HasCount(2, result);
        AssertContainDependency(result,
            (DependencyType.SwiftPackage, "https://example.com/qualified.git", "from: \"1.0.0\"", 2, 74),
            (DependencyType.SwiftPackage, "https://example.com/dependency.git", "from: \"2.0.0\"", 3, 67));
    }

    [Fact]
    public async Task SwiftPackageManifestDependencies_UnbalancedParenthesesAreScannedInLinearTime()
    {
        var content = string.Concat(Enumerable.Repeat(".package(", 50_000)) + """.package(url: "https://example.com/a.git", from: "1.0.0")""";
        var path = _directory.GetFullPath("Package.swift");

        var stopwatch = ValueStopwatch.StartNew();
        var result = await DependencyScanner.ScanFileAsync(_directory.FullPath, path, Encoding.UTF8.GetBytes(content), [new SwiftPackageDependencyScanner()], XunitCancellationToken);
        var elapsed = stopwatch.GetElapsedTime();

        var dependency = Assert.Single(result);
        Assert.Equal("https://example.com/a.git", dependency.Name);
        Assert.Equal("from: \"1.0.0\"", dependency.Version);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Scanning took {elapsed}");
    }

    [Fact]
    public async Task SwiftPackageManifestDependencies_VersionSpecificManifest()
    {
        const string Original = """
            let package = Package(
                dependencies: [
                    .package(url: "https://github.com/apple/swift-nio.git", from: "2.76.0"),
                ]
            )
            """;

        AddFile("Package@swift-5.9.swift", Original);
        AddFile("Package@swift-6.swift", Original);
        AddFile("Package@swift-.swift", Original);
        AddFile("Package@swift-5.9.swift.bak", Original);
        AddFile("Package@swift-latest.swift", Original);

        var result = await GetDependencies<SwiftPackageDependencyScanner>();
        Assert.HasCount(2, result);
        Assert.Single(result, d => Path.GetFileName(d.VersionLocation!.FilePath) == "Package@swift-5.9.swift" && d.Version == "from: \"2.76.0\"");
        Assert.Single(result, d => Path.GetFileName(d.VersionLocation!.FilePath) == "Package@swift-6.swift" && d.Version == "from: \"2.76.0\"");

        var dependency = Assert.Single(result, d => Path.GetFileName(d.VersionLocation!.FilePath) == "Package@swift-5.9.swift");
        await dependency.UpdateVersionAsync("exact: \"2.80.0\"");
        AssertFileContentEqual("Package@swift-5.9.swift", Original.Replace("from: \"2.76.0\"", "exact: \"2.80.0\"", StringComparison.Ordinal), ignoreNewLines: false);
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\r\r\n")]
    public async Task SwiftPackageManifestDependencies_CarriageReturnLineEndings(string newLine)
    {
        var original = "let package = Package(" + newLine + "    dependencies: [" + newLine + "        .package(url: \"https://github.com/apple/swift-nio.git\", from: \"2.76.0\")," + newLine + "    ]" + newLine + ")" + newLine;

        AddFile("Package.swift", original);
        var result = await GetDependencies<SwiftPackageDependencyScanner>();
        var dependency = Assert.Single(result);
        Assert.Equal(newLine == "\r" ? 3 : 5, ((ILocationLineInfo)dependency.VersionLocation!).LineNumber);

        await dependency.UpdateVersionAsync("exact: \"2.80.0\"");
        AssertFileContentEqual("Package.swift", original.Replace("from: \"2.76.0\"", "exact: \"2.80.0\"", StringComparison.Ordinal), ignoreNewLines: false);
    }

    [Fact]
    public async Task AzureDevOpsContainerDependencies()
    {
        AddFile("sample.yml", """
            container: 'image:1.2.3'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            container: 'dummy1:2.3.4'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsContainerWithoutVersionDependencies()
    {
        AddFile("sample.yml", """
            container: 'image'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            container: 'dummy1'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsInvalidYaml()
    {
        AddFile("sample.yml", """
            dummy
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        Assert.Empty(result);
    }

    [Fact]
    public async Task AzureDevOpsJobContainerDependencies()
    {
        AddFile("sample.yml", """
            jobs:
            - job: dummy
              container: 'image:1.2.3'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "image", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            jobs:
            - job: dummy
              container: 'image1:2.3.4'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsDeploymentContainerDependencies()
    {
        AddFile("sample.yml", """
            jobs:
            - deployment: dummy
              container: 'image:1.2.3'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            jobs:
            - deployment: dummy
              container: 'dummy1:2.3.4'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsContainerExpandedDependencies()
    {
        AddFile("sample.yml", """
            container:
                image: 'image:1.2.3'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            container:
                image: 'dummy1:2.3.4'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsVmImageDependencies()
    {
        AddFile("sample.yml", """
            pool:
              vmImage: 'ubuntu-18.04'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "", "windows-latest");
        AssertFileContentEqual("sample.yml", """
            pool:
              vmImage: 'windows-latest'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsJobsVmImageDependencies()
    {
        AddFile("sample.yml", """
            jobs:
            - job: dummy
              pool:
                vmImage: 'ubuntu-18.04'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "", "windows-latest");
        AssertFileContentEqual("sample.yml", """
            jobs:
            - job: dummy
              pool:
                vmImage: 'windows-latest'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsStageVmImageDependencies()
    {
        AddFile("sample.yml", """
            stages:
            - stage: dummy
              pool:
                vmImage: 'ubuntu-18.04'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "", "windows-latest");
        AssertFileContentEqual("sample.yml", """
            stages:
            - stage: dummy
              pool:
                vmImage: 'windows-latest'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesContainerDependencies()
    {
        AddFile("sample.yml", """
            resources:
              containers:
              - container: dummy
                image: image:1.2.3
                registry: 'registry'
                type: ACR
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            resources:
              containers:
              - container: dummy
                image: dummy1:2.3.4
                registry: 'registry'
                type: ACR
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesRepositoryDependencies()
    {
        AddFile("sample.yml", """
            resources:
              repositories:
              - repository: dummy
                name: repo
                endpoint: myendpoint
                ref: 'main'
                type: git
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();

        var dependency = Assert.Single(result, d => d.Type == DependencyType.GitReference);
        Assert.Equal("dummy", dependency.Metadata["repository"]);
        Assert.Equal("myendpoint", dependency.Metadata["endpoint"]);

        await UpdateDependencies(result, "dummy", "1.2.3");
        AssertFileContentEqual("sample.yml", """
            resources:
              repositories:
              - repository: dummy
                name: dummy1
                endpoint: myendpoint
                ref: '1.2.3'
                type: git
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesRepositoryDependencies_NoRef()
    {
        AddFile("sample.yml", """
            resources:
              repositories:
              - repository: dummy
                name: repo
                type: git
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "1.2.3");
        AssertFileContentEqual("sample.yml", """
            resources:
              repositories:
              - repository: dummy
                name: dummy1
                type: git
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesStepsTask()
    {
        AddFile("sample.yml", """
            pool:
                vmImage: 'ubuntu-18.04'
            steps:
            - task: UseDotNet@2
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2");
        AssertFileContentEqual("sample.yml", """
            pool:
                vmImage: '2'
            steps:
            - task: dummy1@2
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesJobsStepsTask()
    {
        AddFile("sample.yml", """
            pool:
                vmImage: 'ubuntu-18.04'
            jobs:
            - job: B
              steps:
              - task: UseDotNet@2
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2");
        AssertFileContentEqual("sample.yml", """
            pool:
                vmImage: '2'
            jobs:
            - job: B
              steps:
              - task: dummy1@2
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesStagesJobsStepsTask()
    {
        AddFile("sample.yml", """
            pool:
                vmImage: 'ubuntu-18.04'
            stages:
            - stage: A
              jobs:
              - job: B
                steps:
                - task: UseDotNet@2
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2");
        AssertFileContentEqual("sample.yml", """
            pool:
                vmImage: '2'
            stages:
            - stage: A
              jobs:
              - job: B
                steps:
                - task: dummy1@2
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsTemplateInSteps()
    {
        AddFile("sample.yml", """
            pool:
                vmImage: 'ubuntu-18.04'
            steps:
              - template: file.yml@templates
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("sample.yml", """
            pool:
                vmImage: '2.0.0'
            steps:
              - template: dummy1@2.0.0
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsTemplatesInStagesJobsAndExtends()
    {
        AddFile("sample.yml", """
            extends:
              template: pipeline.yml@templates
            stages:
            - template: stage.yml@templates
            - stage: A
              jobs:
              - template: job.yml@templates
            """);

        var result = await GetDependencies<AzureDevOpsScanner>();

        Assert.HasCount(3, result);
        AssertContainDependency(result,
            (DependencyType.AzureDevOpsTemplate, "pipeline.yml", "templates", 2, 26),
            (DependencyType.AzureDevOpsTemplate, "stage.yml", "templates", 4, 23),
            (DependencyType.AzureDevOpsTemplate, "job.yml", "templates", 7, 23));
    }

    [Fact]
    public async Task AzureDevOpsDeploymentStrategySteps()
    {
        AddFile("sample.yml", """
            jobs:
            - deployment: Deploy
              strategy:
                runOnce:
                  preDeploy:
                    steps:
                    - task: A@1
                  deploy:
                    pool:
                      vmImage: ubuntu-22.04
                    steps:
                    - task: B@2
                  on:
                    failure:
                      steps:
                      - task: C@3
                    success:
                      steps:
                      - template: steps.yml@templates
            - deployment: Canary
              strategy:
                canary:
                  routeTraffic:
                    steps:
                    - task: D@4
                  postRouteTraffic:
                    steps:
                    - task: E@5
            """);

        var result = await GetDependencies<AzureDevOpsScanner>();

        Assert.HasCount(7, result);
        AssertContainDependency(result,
            (DependencyType.AzureDevOpsTask, "A", "1", 7, 19),
            (DependencyType.AzureDevOpsTask, "B", "2", 12, 19),
            (DependencyType.AzureDevOpsTask, "C", "3", 16, 21),
            (DependencyType.AzureDevOpsTemplate, "steps.yml", "templates", 19, 33),
            (DependencyType.AzureDevOpsTask, "D", "4", 25, 19),
            (DependencyType.AzureDevOpsTask, "E", "5", 28, 19),
            (DependencyType.AzureDevOpsVMPool, null, "ubuntu-22.04", 10, 20));
    }

    [Fact]
    public async Task AzureDevOpsResourcesGitHubAndBitbucketRepositories()
    {
        AddFile("sample.yml", """
            resources:
              repositories:
              - repository: templates
                type: github
                name: owner/repo
                ref: refs/tags/v1.0
                endpoint: github-connection
              - repository: bb
                type: bitbucket
                name: team/repo
                ref: refs/heads/main
                endpoint: bb-connection
            """);

        var result = await GetDependencies<AzureDevOpsScanner>();

        Assert.HasCount(2, result);
        AssertContainDependency(result,
            (DependencyType.GitReference, "owner/repo", "refs/tags/v1.0", 6, 10),
            (DependencyType.GitReference, "team/repo", "refs/heads/main", 11, 10));
        var github = Assert.Single(result, d => d.Name == "owner/repo");
        Assert.Equal("templates", github.Metadata["repository"]);
        Assert.Equal("github-connection", github.Metadata["endpoint"]);
        Assert.Equal("github", github.Metadata["type"]);
    }

    [Fact]
    public async Task AzureDevOpsContainerAliasAndRegistryPort()
    {
        AddFile("sample.yml", """
            resources:
              containers:
              - container: u14
                image: ubuntu:14.04
            jobs:
            - job: A
              container: u14
            - job: B
              container: localhost:5000/img:1.0
            """);

        var result = await GetDependencies<AzureDevOpsScanner>();

        Assert.HasCount(2, result);
        AssertContainDependency(result,
            (DependencyType.DockerImage, "ubuntu", "14.04", 4, 19),
            (DependencyType.DockerImage, "localhost:5000/img", "1.0", 9, 33));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("sample.yml", """
            resources:
              containers:
              - container: u14
                image: dummy1:2.0.0
            jobs:
            - job: A
              container: u14
            - job: B
              container: dummy2:2.0.0
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task HelmChartsDependencies()
    {
        AddFile("Chart.yaml", """
            dependencies:
            - name: nginx
              version: "1.2.3"
              repository: "https://example.com/charts"
            - name: memcached
              version: "3.2.1"
              repository: https://another.example.com/charts
            """);
        var result = await GetDependencies<HelmChartDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.HelmChart, "nginx", "1.2.3", 3, 13),
            (DependencyType.HelmChart, "memcached", "3.2.1", 6, 13));
        Assert.Equal("https://example.com/charts", Assert.Single(result, d => d.Name == "nginx").Metadata["repository"]);
        Assert.Equal("https://another.example.com/charts", Assert.Single(result, d => d.Name == "memcached").Metadata["repository"]);

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Chart.yaml", """
            dependencies:
            - name: dummy1
              version: "2.0.0"
              repository: "https://example.com/charts"
            - name: dummy2
              version: "2.0.0"
              repository: https://another.example.com/charts
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task HelmChartsDependencies_AliasAndRepositoryReferences()
    {
        AddFile("Chart.yaml", """
            dependencies:
            - name: postgresql
              version: 12.1.0
              repository: https://charts.bitnami.com/bitnami
            - name: redis
              alias: cache
              version: 17.0.0
              repository: "@bitnami"
            - name: local-chart
              version: 0.1.0
            """);

        var result = await GetDependencies<HelmChartDependencyScanner>();

        Assert.HasCount(3, result);
        AssertContainDependency(result,
            (DependencyType.HelmChart, "postgresql", "12.1.0", 3, 12),
            (DependencyType.HelmChart, "redis", "17.0.0", 7, 12),
            (DependencyType.HelmChart, "local-chart", "0.1.0", 10, 12));

        var redis = Assert.Single(result, d => d.Name == "redis");
        Assert.Equal("@bitnami", redis.Metadata["repository"]);
        Assert.Equal("cache", redis.Metadata["alias"]);
        Assert.Null(Assert.Single(result, d => d.Name == "local-chart").Metadata["repository"]);
    }

    [Theory]
    [InlineData("renovate.json")]
    [InlineData("renovate.json5")]
    [InlineData(".renovaterc")]
    [InlineData(".renovaterc.json")]
    [InlineData(".renovaterc.json5")]
    [InlineData("renovaterc")]
    [InlineData("renovaterc.json")]
    [InlineData("renovaterc.json5")]
    [InlineData(".github/renovate.json")]
    [InlineData(".github/renovate.json5")]
    [InlineData(".gitlab/renovate.json")]
    [InlineData(".gitlab/renovate.json5")]
    public async Task RenovateExtendsDependencies(string filePath)
    {
        AddFile(filePath, """
            {
              "extends": [
                "config:recommended",
                "github>owner/repo",
                "github>owner/repo#1.2.3",
                "github>owner/repo:file",
                "github>owner/repo:file#1.2.3",
                "github>owner/repo//file",
                "github>owner/repo//file#1.2.3",
              ],
              "packageRules": [
                {
                  "extends": [
                    "github>owner/repo//file#1.2.3"
                  ]
                }
              ]
            }
            """);
        var result = await GetDependencies<RenovateExtendsDependencyScanner>();
        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual(filePath, """
            {
              "extends": [
                "dummy1",
                "dummy2",
                "dummy3#2.0.0",
                "dummy4",
                "dummy5#2.0.0",
                "dummy6",
                "dummy7#2.0.0",
              ],
              "packageRules": [
                {
                  "extends": [
                    "dummy8#2.0.0"
                  ]
                }
              ]
            }
            """, ignoreNewLines: true);
    }

    [Theory]
    [InlineData(".claude-plugin/marketplace.json")]
    [InlineData(".github/plugin/marketplace.json")]
    [InlineData("marketplaces/sample/.claude-plugin/marketplace.json")]
    public async Task AgentPluginMarketplaceDependencies(string filePath)
    {
        AddFile(filePath, """
            {
              "name": "sample",
              "plugins": [
                { "name": "local", "source": "./plugins/local" },
                { "name": "local-without-prefix", "source": "plugins/local" },
                { "name": "github", "source": { "source": "github", "repo": "owner/github", "ref": "v1.0.0" } },
                { "name": "github-sha", "source": { "source": "github", "repo": "owner/github-sha", "ref": "v2.0.0", "sha": "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0", "path": "plugins/sha" } },
                { "name": "url", "source": { "source": "url", "url": "https://gitlab.com/team/plugin.git", "ref": "main" } },
                { "name": "subdir", "source": { "source": "git-subdir", "url": "https://github.com/acme/monorepo.git", "path": "tools/plugin" } },
                { "name": "npm", "source": { "source": "npm", "package": "@acme/plugin", "version": "2.1.0", "registry": "https://npm.example.com" } },
                { "name": "archive", "source": { "source": "archive", "url": "https://example.com/plugin.zip" } },
                { "name": "shorthand", "source": "owner/shorthand#v3.0.0" },
                { "name": "git-url", "source": "https://github.com/owner/git-url.git" },
              ]
            }
            """);
        var result = await GetDependencies<AgentPluginDependencyScanner>();
        Assert.HasCount(7, result);
        AssertContainDependency(result,
            (DependencyType.AgentPlugin, "owner/github", "v1.0.0", 0, 0),
            (DependencyType.AgentPlugin, "owner/github-sha", "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0", 0, 0),
            (DependencyType.AgentPlugin, "https://gitlab.com/team/plugin.git", "main", 0, 0),
            (DependencyType.AgentPlugin, "https://github.com/acme/monorepo.git", null, 0, 0),
            (DependencyType.Npm, "@acme/plugin", "2.1.0", 0, 0),
            (DependencyType.AgentPlugin, "owner/shorthand", "v3.0.0", 0, 0),
            (DependencyType.AgentPlugin, "https://github.com/owner/git-url.git", null, 0, 0));

        var githubSha = Assert.Single(result, d => d.Name == "owner/github-sha");
        Assert.Equal("github-sha", githubSha.Metadata["plugin"]);
        Assert.Equal("v2.0.0", githubSha.Metadata["ref"]);
        Assert.Equal("plugins/sha", githubSha.Metadata["path"]);
        Assert.Equal("tools/plugin", Assert.Single(result, d => d.Name == "https://github.com/acme/monorepo.git").Metadata["path"]);
        Assert.Equal("https://npm.example.com", Assert.Single(result, d => d.Name == "@acme/plugin").Metadata["registry"]);

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual(filePath, """
            {
              "name": "sample",
              "plugins": [
                { "name": "local", "source": "./plugins/local" },
                { "name": "local-without-prefix", "source": "plugins/local" },
                { "name": "github", "source": { "source": "github", "repo": "dummy1", "ref": "2.0.0" } },
                { "name": "github-sha", "source": { "source": "github", "repo": "dummy2", "ref": "v2.0.0", "sha": "2.0.0", "path": "plugins/sha" } },
                { "name": "url", "source": { "source": "url", "url": "dummy3", "ref": "2.0.0" } },
                { "name": "subdir", "source": { "source": "git-subdir", "url": "dummy4", "path": "tools/plugin" } },
                { "name": "npm", "source": { "source": "npm", "package": "dummy5", "version": "2.0.0", "registry": "https://npm.example.com" } },
                { "name": "archive", "source": { "source": "archive", "url": "https://example.com/plugin.zip" } },
                { "name": "shorthand", "source": "dummy6#2.0.0" },
                { "name": "git-url", "source": "dummy7" },
              ]
            }
            """, ignoreNewLines: true);
    }

    [Theory]
    [InlineData(".claude/settings.json")]
    [InlineData(".claude/settings.local.json")]
    [InlineData(".github/copilot/settings.json")]
    [InlineData(".github/copilot/settings.local.json")]
    [InlineData("src/app/.claude/settings.json")]
    public async Task AgentPluginSettingsDependencies(string filePath)
    {
        AddFile(filePath, """
            {
              // Marketplaces shared with the team
              "extraKnownMarketplaces": {
                "github": { "source": { "source": "github", "repo": "owner/marketplace" } },
                "github-ref": { "source": { "source": "github", "repo": "owner/marketplace-ref", "ref": "v1.0.0" } },
                "git": { "source": { "source": "git", "url": "https://gitlab.com/team/marketplace.git", "ref": "main" } },
                "url": { "source": { "source": "url", "url": "https://example.com/marketplace.json" } },
                "directory": { "source": { "source": "directory", "path": "./local-marketplace" } },
                "flat": { "source": "github", "repo": "owner/flat", "ref": "v2.0.0", "autoUpdate": true },
              },
              "enabledPlugins": {
                "plugin@github": true
              }
            }
            """);
        var result = await GetDependencies<AgentPluginDependencyScanner>();
        Assert.HasCount(5, result);
        AssertContainDependency(result,
            (DependencyType.AgentPluginMarketplace, "owner/marketplace", null, 0, 0),
            (DependencyType.AgentPluginMarketplace, "owner/marketplace-ref", "v1.0.0", 0, 0),
            (DependencyType.AgentPluginMarketplace, "https://gitlab.com/team/marketplace.git", "main", 0, 0),
            (DependencyType.AgentPluginMarketplace, "https://example.com/marketplace.json", null, 0, 0),
            (DependencyType.AgentPluginMarketplace, "owner/flat", "v2.0.0", 0, 0));
        Assert.Equal("github-ref", Assert.Single(result, d => d.Name == "owner/marketplace-ref").Metadata["marketplace"]);

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual(filePath, """
            {
              // Marketplaces shared with the team
              "extraKnownMarketplaces": {
                "github": { "source": { "source": "github", "repo": "dummy1" } },
                "github-ref": { "source": { "source": "github", "repo": "dummy2", "ref": "2.0.0" } },
                "git": { "source": { "source": "git", "url": "dummy3", "ref": "2.0.0" } },
                "url": { "source": { "source": "url", "url": "dummy4" } },
                "directory": { "source": { "source": "directory", "path": "./local-marketplace" } },
                "flat": { "source": "github", "repo": "dummy5", "ref": "2.0.0", "autoUpdate": true },
              },
              "enabledPlugins": {
                "plugin@github": true
              }
            }
            """, ignoreNewLines: true);
    }

    [Theory]
    [InlineData("marketplace.json")]
    [InlineData(".github/marketplace.json")]
    [InlineData("plugin/marketplace.json")]
    [InlineData(".claude/marketplace.json")]
    [InlineData("settings.json")]
    [InlineData(".vscode/settings.json")]
    [InlineData(".github/settings.json")]
    [InlineData("copilot/settings.json")]
    [InlineData(".claude-plugin/settings.json")]
    public async Task AgentPluginDependencies_IgnoresOtherFiles(string filePath)
    {
        AddFile(filePath, """
            {
              "plugins": [
                { "name": "github", "source": { "source": "github", "repo": "owner/plugin", "ref": "v1.0.0" } }
              ],
              "extraKnownMarketplaces": {
                "github": { "source": { "source": "github", "repo": "owner/marketplace", "ref": "v1.0.0" } }
              }
            }
            """);
        var result = await GetDependencies<AgentPluginDependencyScanner>();
        Assert.Empty(result);
    }

    private async Task<Dependency[]> GetDependencies<T>(ImmutableArray<DependencyScanner>? scanners = null) where T : DependencyScanner
    {
        var options = new ScannerOptions { DegreeOfParallelism = 1 };
        if (scanners is not null)
        {
            options.Scanners = scanners.Value;
        }

        var dependencies = await Scan(options);
        foreach (var dep in dependencies)
        {
            testOutputHelper.WriteLine($"- {dep}");
        }

        // Validate dependencies
        foreach (var dep in dependencies)
        {
            if (dep.Name is not null)
            {
                Assert.NotNull(dep.NameLocation);
            }

            if (dep.Version is not null)
            {
                Assert.NotNull(dep.VersionLocation);
            }
        }

        // Ensure getting scanning in parallel gives the same result
        options.DegreeOfParallelism = 16;
        var dependencies2 = await Scan(options);
        Assert.HasCount(dependencies.Length, dependencies2);
        return dependencies;

        async Task<Dependency[]> Scan(ScannerOptions options)
        {
            var items = await DependencyScanner.ScanDirectoryAsync(_directory.FullPath, options);
            var scannerType = typeof(T).FullName ?? throw new InvalidOperationException("Type full name should not be null");
            return items.Where(d => d.Tags.Contains(scannerType)).ToArray();
        }
    }

    private sealed record DetectedDependency(Dependency Dependency, Location Location, Func<Task> UpdateText);

    private static async Task UpdateDependencies(IEnumerable<Dependency> dependencies, string newName, string newVersion)
    {
        // dep name often have unique names (json, yaml, etc.)
        var i = dependencies.Count(d => d.NameLocation is not null && d.NameLocation.IsUpdatable);

        var allLocations = dependencies
            .SelectMany(d => new DetectedDependency[]
            {
                new(d, d.NameLocation!, () => d.UpdateNameAsync(newName + i--.ToStringInvariant())),
                new(d, d.VersionLocation!, () => d.UpdateVersionAsync(newVersion)),
            })
            .Where(item => item.Location is not null);

        // Group by file location and order by position desc
        foreach (var locationsByFile in allLocations.Where(item => item.Location.IsUpdatable).GroupBy(item => item.Location.FilePath, StringComparer.Ordinal))
        {
            var locationsWithLineInfo = locationsByFile
                .Where(item => item.Location is ILocationLineInfo)
                .OrderByDescending(item => (((ILocationLineInfo)item.Location).LineNumber, ((ILocationLineInfo)item.Location).LinePosition))
                .ToArray();

            // No line info to sort on, so fall back to the reverse of the discovery order to keep updating from the
            // end of the file like the branch above. Relying on the scan order instead only worked by accident, back
            // when the scanner returned a ConcurrentBag and its enumeration happened to be the reverse of insertion.
            var locationWithoutLineInfo = locationsByFile.Where(item => item.Location is not ILocationLineInfo).Reverse().ToArray();

            foreach (var item in locationsWithLineInfo)
            {

                await item.UpdateText().ConfigureAwait(false);
            }

            foreach (var item in locationWithoutLineInfo)
            {
                await item.UpdateText().ConfigureAwait(false);
            }
        }
    }

    private void AddFile(string path, string content) => AddFile(path, content, Encoding.UTF8);

    private void AddFile(string path, string content, Encoding encoding) => AddFile(path, encoding.GetBytes(content));

    private void AddFile(string path, byte[] content)
    {
        var fullPath = _directory.GetFullPath(path);
        Directory.CreateDirectory(fullPath.Parent);
        File.WriteAllBytes(fullPath, content);
    }

    private static void AssertContainDependency(IEnumerable<Dependency> dependencies, params (DependencyType Type, string? Name, string? Version, int VersionLine, int VersionColumn)[] expectedDependencies)
    {
        foreach (var expected in expectedDependencies)
        {
            Assert.Contains(dependencies, d =>
                d.Type == expected.Type &&
                d.Name == expected.Name &&
                d.Version == expected.Version &&
                (expected.VersionLine == 0 || (d.VersionLocation as ILocationLineInfo)?.LineNumber == expected.VersionLine) &&
                (expected.VersionColumn == 0 || (d.VersionLocation as ILocationLineInfo)?.LinePosition == expected.VersionColumn));
        }
    }

    private void AssertFileContentEqual(string path, string expected, bool ignoreNewLines)
    {
        var fullPath = _directory.GetFullPath(path);
        var actual = File.ReadAllText(fullPath);
        if (ignoreNewLines)
        {
            actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal);
            expected = expected.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        Assert.Equal(expected, actual);
    }

    public void Dispose()
    {
        _directory.Dispose();
    }

    [GeneratedRegex("image: (?<name>[a-z]+):(?<version>[0-9]+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10000)]
    private static partial Regex DockerImageWithVersionRegex();

    [GeneratedRegex("image: (?<name>[a-z]+)(:(?<version>[0-9]+))?", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10000)]
    private static partial Regex DockerImageWithOptionalVersionRegex();
}
