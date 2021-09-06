using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meziantou.Extensions.Logging.InMemory.Tests
{
    public sealed class InMemoryLoggerTests
    {
        [Fact]
        public void WithoutScope()
        {
            var logger = new InMemoryLogger("my_category");
            logger.LogInformation("Test");

            var log = logger.Logs.Informations.Single();
            log.Message.Should().Be("Test");
            log.State.Should().BeEquivalentTo(new[] { KeyValuePair.Create<string, object>("{OriginalFormat}", "Test") });
            log.Scopes.Should().BeEquivalentTo(System.Array.Empty<object>());

            log.ToString().Should().Be("[my_category] Information: Test\n  => [{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test\"}]");
        }

        [Fact]
        public void WithScope()
        {
            var logger = new InMemoryLogger("my_category");
            using (logger.BeginScope(new { Name = "test" }))
            using (logger.BeginScope(new { Age = 52, Name = "John" }))
            {
                logger.LogInformation("Test {Number}", 1);
            }

            var log = logger.Logs.Informations.Single();
            log.Message.Should().Be("Test 1");
            log.State.Should().BeEquivalentTo(new[] { KeyValuePair.Create<string, object>("Number", 1), KeyValuePair.Create<string, object>("{OriginalFormat}", "Test {Number}") });
            log.Scopes.Should().BeEquivalentTo(new object[] { new { Age = 52, Name = "John" }, new { Name = "test" } });

            log.ToString().Should().Be("[my_category] Information: Test 1\n  => [{\"Key\":\"Number\",\"Value\":1},{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test {Number}\"}]\n  => {\"Name\":\"test\"}\n  => {\"Age\":52,\"Name\":\"John\"}");
        }
    }
}
