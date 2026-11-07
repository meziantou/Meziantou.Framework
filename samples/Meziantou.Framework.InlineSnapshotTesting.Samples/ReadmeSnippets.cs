using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

/// <summary>The examples of src/Meziantou.Framework.InlineSnapshotTesting/readme.md that cannot run as tests.</summary>
public static class ReadmeSnippets
{
    public static void GettingStarted()
    {
        #region InlineSnapshot_GettingStarted
        var data = new
        {
            FirstName = "Gérald",
            LastName = "Barré",
            NickName = "meziantou",
        };

        // No need to write the expected value
        InlineSnapshot.Validate(data);
        #endregion
    }

    public static void UseMergeTool()
    {
        #region InlineSnapshot_UseMergeTool
        InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
        };
        #endregion
    }

    public static void RegisterSourceRootMapping()
    {
        #region InlineSnapshot_RegisterSourceRootMapping
        InlineSnapshot.RegisterSourceRootMapping("/_/", @"C:/agent/_work/1/s/");
        #endregion
    }

    public static void FormatHtml(HttpContent response)
    {
        #region InlineSnapshot_HtmlFormatter
        // Requires the Meziantou.Framework.HumanReadableSerializer.Html package
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.AddHtmlFormatter(new HtmlFormatterOptions
                {
                    OrderAttributes = true,
                    AttributeQuote = HtmlAttributeQuote.DoubleQuote,
                    RedactContentSecurityPolicyNonce = true,
                });
            })
            .Validate(response);
        #endregion
    }

    public static void ForceUpdateSnapshots()
    {
        #region InlineSnapshot_ForceUpdateSnapshots
        InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
        {
            ForceUpdateSnapshots = true, // Override the snapshot even if the value matches the expected value
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
        };
        #endregion
    }
}
