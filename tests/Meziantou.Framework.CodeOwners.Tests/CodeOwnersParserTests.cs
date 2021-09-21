using System.Linq;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.CodeOwners.Tests
{
    public sealed class CodeOwnersParserTests
    {
        [Fact]
        public void ParseEmptyCodeOwners()
        {
            var actual = CodeOwnersParser.Parse("").ToArray();
            actual.Should().BeEmpty();
        }

        [Fact]
        public void ParseSingleLineCodeOwners()
        {
            var actual = CodeOwnersParser.Parse("* @user1 @user2").ToArray();

            var expected = new CodeOwnersEntry[]
            {
                CodeOwnersEntry.FromUsername("*", "user1"),
                CodeOwnersEntry.FromUsername("*", "user2"),
            };

            actual.Should().Equal(expected);
        }

        [Fact]
        public void ParseCodeOwners()
        {
            const string Content = "\n" +
                                   "# This is a comment.\n" +
                                   "# Each line is a file pattern followed by one or more owners.\n" +
                                   "\n" +
                                   "# These owners will be the default owners for everything in\n" +
                                   "# the repo. Unless a later match takes precedence,\n" +
                                   "# @global-owner1 and @global-owner2 will be requested for\n" +
                                   "# review when someone opens a pull request.\n" +
                                   "*       @global-owner1 @global-owner2\n" +
                                   "\n" +
                                   "# Order is important; the last matching pattern takes the most\n" +
                                   "# precedence. When someone opens a pull request that only\n" +
                                   "# modifies JS files, only @js-owner and not the global\n" +
                                   "# owner(s) will be requested for a review.\n" +
                                   "*.js    @js-owner\n" +
                                   "\n" +
                                   "# You can also use email addresses if you prefer. They'll be\n" +
                                   "# used to look up users just like we do for commit author\n" +
                                   "# emails.\n" +
                                   "*.go docs@example.com\n" +
                                   "\n" +
                                   "# In this example, @doctocat owns any files in the build/logs\n" +
                                   "# directory at the root of the repository and any of its\n" +
                                   "# subdirectories.\n" +
                                   "/build/logs/ @doctocat\n" +
                                   "\n" +
                                   "# The `docs/*` pattern will match files like\n" +
                                   "# `docs/getting-started.md` but not further nested files like\n" +
                                   "# `docs/build-app/troubleshooting.md`.\n" +
                                   "docs/*  docs@example.com\n" +
                                   "\n" +
                                   "# In this example, @octocat owns any file in an apps directory\n" +
                                   "# anywhere in your repository.\n" +
                                   "apps/ @octocat\n" +
                                   "\n" +
                                   "# In this example, @doctocat owns any file in the `/docs`\n" +
                                   "# directory in the root of your repository.\n" +
                                   "/docs/ @doctocat\n";

            var actual = CodeOwnersParser.Parse(Content).ToArray();

            var expected = new CodeOwnersEntry[]
            {
                CodeOwnersEntry.FromUsername("*", "global-owner1"),
                CodeOwnersEntry.FromUsername("*", "global-owner2"),
                CodeOwnersEntry.FromUsername("*.js", "js-owner"),
                CodeOwnersEntry.FromEmailAddress("*.go", "docs@example.com"),
                CodeOwnersEntry.FromUsername("/build/logs/", "doctocat"),
                CodeOwnersEntry.FromEmailAddress("docs/*", "docs@example.com"),
                CodeOwnersEntry.FromUsername("apps/", "octocat"),
                CodeOwnersEntry.FromUsername("/docs/", "doctocat"),
            };

            actual.Should().Equal(expected);
        }

        [Fact]
        public void ParseLineEndingWithSpaces()
        {
            var actual = CodeOwnersParser.Parse("* @user1 @user2  ").ToArray();

            var expected = new CodeOwnersEntry[]
            {
                CodeOwnersEntry.FromUsername("*", "user1"),
                CodeOwnersEntry.FromUsername("*", "user2"),
            };

            actual.Should().Equal(expected);
        }

        [Fact]
        public void ParseTwice()
        {
            const string Content = "* @user1 @user2  ";
            var parse1 = CodeOwnersParser.Parse(Content).ToArray();
            var parse2 = CodeOwnersParser.Parse(Content).ToArray();
            parse1.Should().Equal(parse2);
        }

        [Fact]
        public void ParseCodeOwnersWithSections()
        {
            const string Content = "\n" +
                                   "[Section]\n" +
                                   "* @user1 @user2\n" +
                                   "\n" +
                                   "^[Optional Section]\n" +
                                   "*.js @user2 @user3\n";

            var actual = CodeOwnersParser.Parse(Content).ToArray();

            var expected = new CodeOwnersEntry[]
            {
                CodeOwnersEntry.FromUsername("*", "user1"),
                CodeOwnersEntry.FromUsername("*", "user2"),
                CodeOwnersEntry.FromUsername("*.js", "user2"),
                CodeOwnersEntry.FromUsername("*.js", "user3")
            };

            actual.Should().Equal(expected);
        }
    }
}
