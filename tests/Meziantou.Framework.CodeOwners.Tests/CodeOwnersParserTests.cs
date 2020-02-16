using System.Linq;
using Xunit;

namespace Meziantou.Framework.CodeOwners.Tests
{
    public sealed class CodeOwnersParserTests
    {
        [Fact]
        public void ParseEmptyCodeOwners()
        {
            var actual = CodeOwnersParser.Parse("").ToArray();
            Assert.Empty(actual);
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

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ParseCodeOwners()
        {
            const string Content = @"
# This is a comment.
# Each line is a file pattern followed by one or more owners.

# These owners will be the default owners for everything in
# the repo. Unless a later match takes precedence,
# @global-owner1 and @global-owner2 will be requested for
# review when someone opens a pull request.
*       @global-owner1 @global-owner2

# Order is important; the last matching pattern takes the most
# precedence. When someone opens a pull request that only
# modifies JS files, only @js-owner and not the global
# owner(s) will be requested for a review.
*.js    @js-owner

# You can also use email addresses if you prefer. They'll be
# used to look up users just like we do for commit author
# emails.
*.go docs@example.com

# In this example, @doctocat owns any files in the build/logs
# directory at the root of the repository and any of its
# subdirectories.
/build/logs/ @doctocat

# The `docs/*` pattern will match files like
# `docs/getting-started.md` but not further nested files like
# `docs/build-app/troubleshooting.md`.
docs/*  docs@example.com

# In this example, @octocat owns any file in an apps directory
# anywhere in your repository.
apps/ @octocat

# In this example, @doctocat owns any file in the `/docs`
# directory in the root of your repository.
/docs/ @doctocat
";

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

            Assert.Equal(expected, actual);
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

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ParseTwice()
        {
            const string Content = "* @user1 @user2  ";
            Assert.Equal(CodeOwnersParser.Parse(Content).ToArray(), CodeOwnersParser.Parse(Content).ToArray());
        }
    }
}
