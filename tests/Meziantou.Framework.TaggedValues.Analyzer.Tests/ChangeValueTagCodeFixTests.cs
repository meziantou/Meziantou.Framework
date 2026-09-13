using ChangeValueTagCodeFixProviderType = Meziantou.Framework.Analyzers.TaggedValues.ChangeValueTagCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class ChangeValueTagCodeFixTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task ChangesTheTagOfTheParameter()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M([ValueTag("ProjectId")] Guid projectId) => Load({|MFTV0002:projectId|});
            }
            """,
            """
            class Sample
            {
                static void Load([ValueTag("ProjectId")] Guid id) { }

                void M([ValueTag("ProjectId")] Guid projectId) => Load(projectId);
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheProperty()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                [Obsolete]
                [ValueTag("OrderId")]
                public Guid Id { get; set; }

                void M([ValueTag("ProjectId", "CustomerId")] Guid projectId) => Id = {|MFTV0002:projectId|};
            }
            """,
            """
            class Sample
            {
                [Obsolete]
                [ValueTag("CustomerId", "ProjectId")]
                public Guid Id { get; set; }

                void M([ValueTag("ProjectId", "CustomerId")] Guid projectId) => Id = projectId;
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheReturnValue()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                [return: ValueTag("OrderId")]
                Guid M([ValueTag("ProjectId")] Guid projectId) => {|MFTV0002:projectId|};
            }
            """,
            """
            class Sample
            {
                [return: ValueTag("ProjectId")]
                Guid M([ValueTag("ProjectId")] Guid projectId) => projectId;
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheField_ForDictionaries()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                [ValueTag(Key = "OrderId", Value = "ProjectId")]
                Dictionary<Guid, Guid> _first = [];

                [ValueTag(Key = "OrderId", Value = "CustomerId")]
                Dictionary<Guid, Guid> _second = [];

                void M() => _first = {|MFTV0002:_second|};
            }
            """,
            """
            class Sample
            {
                [ValueTag(Key = "OrderId", Value = "CustomerId")]
                Dictionary<Guid, Guid> _first = [];

                [ValueTag(Key = "OrderId", Value = "CustomerId")]
                Dictionary<Guid, Guid> _second = [];

                void M() => _first = _second;
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheLocalComment()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Guid /* ValueTag=OrderId */ id = {|MFTV0002:projectId|};
                }
            }
            """,
            """
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Guid /* ValueTag=ProjectId */ id = projectId;
                }
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheLocalLineComment()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    // ValueTag=OrderId
                    Guid id = {|MFTV0002:projectId|};
                }
            }
            """,
            """
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    // ValueTag=ProjectId
                    Guid id = projectId;
                }
            }
            """);
    }

    [Fact]
    public async Task CopiesTheTagOfTheOverriddenMember()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            abstract class Base
            {
                [return: ValueTag("OrderId")]
                public abstract Guid GetId();
            }

            class Derived : Base
            {
                [return: ValueTag("ProjectId")]
                public override Guid {|MFTV0004:GetId|}() => Guid.Empty;
            }
            """,
            """
            abstract class Base
            {
                [return: ValueTag("OrderId")]
                public abstract Guid GetId();
            }

            class Derived : Base
            {
                [return: ValueTag("OrderId")]
                public override Guid GetId() => Guid.Empty;
            }
            """);
    }

    [Fact]
    public async Task NoCodeFix_WhenTheSourceTagIsInferredFromItsName()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M(Guid projectId) => Load({|MFTV0002:projectId|});
            }
            """,
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M(Guid projectId) => Load({|MFTV0002:projectId|});
            }
            """,
            inferTagsFromNames: true);
    }
}
