namespace Meziantou.Framework.Tests;

public sealed class LocalTagTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task CommentTagsLocals()
    {
        await VerifyAsync("""
            class Sample
            {
                void M()
                {
                    Guid /* ValueTag=OrderId */ orderId1 = Guid.NewGuid();
                    var orderId2 /* ValueTag=OrderId */ = Guid.NewGuid();
                    /* ValueTag=ProjectId */ Guid projectId = Guid.NewGuid();

                    _ = orderId1 == orderId2;
                    _ = {|MFTV0001:orderId1 == projectId|};
                }
            }
            """);
    }

    [Fact]
    public async Task CommentTagsEveryDeclaratorOrOnlyOne()
    {
        await VerifyAsync("""
            class Sample
            {
                void M()
                {
                    Guid /* ValueTag=OrderId */ a = Guid.Empty, b = Guid.Empty;
                    Guid c = Guid.Empty, d /* ValueTag=ProjectId */ = Guid.Empty;

                    _ = a == b;
                    _ = {|MFTV0001:a == d|};
                    _ = c == a;
                }
            }
            """);
    }

    [Fact]
    public async Task CommentTagsUnionsAndDictionaries()
    {
        await VerifyAsync("""
            class Sample
            {
                static void LoadOrder([ValueTag("OrderId")] Guid orderId) { }
                static void LoadCustomer([ValueTag("CustomerId")] Guid customerId) { }

                void M(Dictionary<Guid, Guid> map)
                {
                    Guid /* ValueTag=OrderId, ProjectId */ id = Guid.Empty;
                    LoadOrder(id);
                    LoadCustomer({|MFTV0002:id|});

                    var /* ValueTag Key=OrderId Value=CustomerId */ lookup = map;
                    LoadOrder(lookup.Keys.First());
                    LoadOrder({|MFTV0002:lookup.Values.First()|});
                }
            }
            """);
    }

    [Fact]
    public async Task CommentTagsForEachOutAndPatternVariables()
    {
        await VerifyAsync("""
            class Sample
            {
                static void LoadOrder([ValueTag("OrderId")] Guid orderId) { }
                static bool TryGet(out Guid value) { value = Guid.Empty; return true; }

                void M(IEnumerable<Guid> ids, object value)
                {
                    foreach (Guid /* ValueTag=ProjectId */ id in ids)
                    {
                        LoadOrder({|MFTV0002:id|});
                    }

                    if (TryGet(out var /* ValueTag=ProjectId */ projectId))
                    {
                        LoadOrder({|MFTV0002:projectId|});
                    }

                    if (value is Guid /* ValueTag=ProjectId */ patternId)
                    {
                        LoadOrder({|MFTV0002:patternId|});
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task CommentTagsUsingAndForVariables()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("ProjectIndex")] int projectIndex)
                {
                    using var /* ValueTag=OrderStream */ orderStream = new System.IO.MemoryStream();
                    using System.IO.MemoryStream /* ValueTag=ProjectStream */ projectStream = new();
                    _ = {|MFTV0001:orderStream == projectStream|};

                    for (int /* ValueTag = OrderIndex */ i = 0; {|MFTV0001:i < projectIndex|}; i++)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TypeArgumentCommentsTagNewExpressions()
    {
        await VerifyAsync("""
            class Sample
            {
                static void LoadOrder([ValueTag("OrderId")] Guid orderId) { }

                void M()
                {
                    var projectIdByOrderId = new Dictionary</* ValueTag=OrderId */ Guid, /* ValueTag=ProjectId */ Guid>();
                    LoadOrder(projectIdByOrderId.Keys.First());
                    LoadOrder({|MFTV0002:projectIdByOrderId.Values.First()|});

                    var projectIds = new List</* ValueTag=ProjectId, CustomerId */ Guid>();
                    LoadOrder({|MFTV0002:projectIds[0]|});

                    var projectIdsByOrderId = new System.Collections.Generic.Dictionary<Guid /* ValueTag=OrderId */, List</* ValueTag=ProjectId */ Guid>>();
                    LoadOrder({|MFTV0002:projectIdsByOrderId[Guid.Empty][0]|});

                    var pair = new KeyValuePair<Guid, /* ValueTag=ProjectId */ Guid>(Guid.Empty, Guid.Empty);
                    LoadOrder({|MFTV0002:pair.Value|});
                }
            }
            """);
    }

    [Fact]
    public async Task TypeArgumentCommentsTagDeclaredTypes()
    {
        await VerifyAsync("""
            class Sample
            {
                static void LoadOrder([ValueTag("OrderId")] Guid orderId) { }

                void M(Dictionary<Guid, Guid> map, object value)
                {
                    Dictionary</* ValueTag=OrderId */ Guid, /* ValueTag=ProjectId */ Guid> projectIdByOrderId = map;
                    LoadOrder({|MFTV0002:projectIdByOrderId.Values.First()|});

                    foreach (KeyValuePair</* ValueTag=ProjectId */ Guid, Guid> pair in map)
                    {
                        LoadOrder({|MFTV0002:pair.Key|});
                    }

                    if (value is List</* ValueTag=ProjectId */ Guid> ids)
                    {
                        LoadOrder({|MFTV0002:ids[0]|});
                    }

                    Guid? /* ValueTag=OrderId */ nullableId = null;
                    Nullable</* ValueTag=ProjectId */ Guid> nullableProjectId = null;
                    _ = {|MFTV0001:nullableId == nullableProjectId|};
                }
            }
            """);
    }

    [Fact]
    public async Task TypeArgumentCommentsCheckInitializersAndConstructorArguments()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId, [ValueTag("ProjectId")] List<Guid> projectIds)
                {
                    _ = new List</* ValueTag=OrderId */ Guid> { orderId, {|MFTV0002:projectId|} };
                    _ = new List</* ValueTag=OrderId */ Guid>({|MFTV0002:projectIds|});
                    _ = new Dictionary</* ValueTag=OrderId */ Guid, /* ValueTag=ProjectId */ Guid>
                    {
                        [orderId] = projectId,
                        [{|MFTV0002:projectId|}] = {|MFTV0002:orderId|},
                    };
                }
            }
            """);
    }

    [Fact]
    public async Task VariableCommentWinsOverTypeArgumentComment()
    {
        await VerifyAsync("""
            class Sample
            {
                void M()
                {
                    var /* ValueTag=OrderId */ ids = {|MFTV0002:new List</* ValueTag=ProjectId */ Guid>()|};
                    List</* ValueTag=OrderId */ Guid> /* ValueTag=ProjectId */ other = [];
                    _ = {|MFTV0001:ids[0] == other[0]|};
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_WhenCommentDisagreesWithInitializer()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Guid /* ValueTag=OrderId */ orderId = {|MFTV0002:projectId|};
                    orderId = {|MFTV0002:projectId|};
                }
            }
            """);
    }

    [Fact]
    public async Task InfersTagsFromInitializers()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("OrderId")] public Guid Id { get; set; }
            }

            class Sample
            {
                static void LoadProject([ValueTag("ProjectId")] Guid projectId) { }

                void M(Order order, object value)
                {
                    var id = order.Id;
                    var copy = id;
                    LoadProject({|MFTV0002:copy|});

                    var fresh = Guid.NewGuid();
                    LoadProject(fresh);

                    if (order.Id is var patternId)
                    {
                        LoadProject({|MFTV0002:patternId|});
                    }

                    switch (order.Id)
                    {
                        case var switchId:
                            LoadProject({|MFTV0002:switchId|});
                            break;
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_WhenInferredLocalIsReassigned()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId)
                {
                    var id = orderId;
                    id = Guid.Empty;
                    id = {|MFTV0002:projectId|};
                }
            }
            """);
    }

    [Fact]
    public async Task InfersTagsFromOutParameters()
    {
        await VerifyAsync("""
            class Sample
            {
                static bool TryGetOrderId([ValueTag("OrderId")] out Guid orderId) { orderId = Guid.Empty; return true; }
                static void LoadProject([ValueTag("ProjectId")] Guid projectId) { }

                void M()
                {
                    if (TryGetOrderId(out var id))
                    {
                        LoadProject({|MFTV0002:id|});
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task OtherCommentsAreIgnored()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Guid /* ValueTagging is not a tag */ a = projectId;
                    Guid /* some comment */ b = projectId;
                    // ValueTagging is not a tag
                    Guid c = projectId;
                    /// ValueTag=OrderId
                    Guid d = projectId;
                }
            }
            """);
    }

    [Fact]
    public async Task LineCommentTagsTheVariablesOfTheNextStatement()
    {
        await VerifyAsync("""
            class Sample
            {
                static void LoadOrder([ValueTag("OrderId")] Guid orderId) { }

                void M([ValueTag("ProjectId")] Guid projectId, IEnumerable<Guid> ids, [ValueTag("ProjectIndex")] int projectIndex)
                {
                    // ValueTag=OrderId
                    Guid a = Guid.Empty, b = Guid.Empty;
                    _ = a == b;
                    _ = {|MFTV0001:a == projectId|};

                    // ValueTag = OrderId, CustomerId
                    // The id of the order being processed
                    var c = Guid.Empty;
                    LoadOrder(c);

                    // ValueTag Key=OrderId Value=ProjectId
                    var map = new Dictionary<Guid, Guid>();
                    LoadOrder({|MFTV0002:map.Values.First()|});

                    // ValueTag=OrderId
                    foreach (var id in ids)
                    {
                        _ = {|MFTV0001:id == projectId|};
                    }

                    // ValueTag=OrderIndex
                    for (var i = 0; {|MFTV0001:i < projectIndex|}; i++)
                    {
                    }

                    // ValueTag=OrderStream
                    using var orderStream = new System.IO.MemoryStream();
                    // ValueTag=ProjectStream
                    using var projectStream = new System.IO.MemoryStream();
                    _ = {|MFTV0001:orderStream == projectStream|};
                }
            }
            """);
    }

    [Fact]
    public async Task InlineCommentWinsOverLineComment()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] Guid orderId)
                {
                    // ValueTag=OrderId
                    Guid a = Guid.Empty, b /* ValueTag=ProjectId */ = Guid.Empty;
                    _ = a == orderId;
                    _ = {|MFTV0001:b == orderId|};
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_WhenLineCommentDisagreesWithInitializer()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    // ValueTag=OrderId
                    var orderId = {|MFTV0002:projectId|};
                }
            }
            """);
    }
}
