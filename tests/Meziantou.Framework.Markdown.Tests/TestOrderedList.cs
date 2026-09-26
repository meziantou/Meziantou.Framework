// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Tests;

public class TestOrderedList
{
    [Fact]
    public void TestReplace()
    {
        var list = new OrderedList<ITest>
        {
            new A(),
            new B(),
            new C(),
        };

        // Replacing B with D. Order should now be A, D, B.
        var result = list.Replace<B>(new D());
        Assert.True(result);
        Assert.HasCount(3, list);
        Assert.IsAssignableTo<A>(list[0]);
        Assert.IsAssignableTo<D>(list[1]);
        Assert.IsAssignableTo<C>(list[2]);

        // Replacing B again should fail, as it's no longer in the list.
        Assert.False(list.Replace<B>(new D()));
    }

    #region Test fixtures
    private interface ITest { }
    private sealed class A : ITest { }
    private sealed class B : ITest { }
    private sealed class C : ITest { }
    private sealed class D : ITest { }
    #endregion
}