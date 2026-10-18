using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#form-submission-algorithm</summary>
public sealed class FormSubmissionTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly RecordingLoader _loader = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = _loader });
        var document = context.Navigate("https://example.com/dir/page.html", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private DocumentRequest LastDocumentRequest => _loader.Requests.Last(r => r.Mode == RequestMode.Navigate);

    [Fact]
    public void Get_NavigatesTheTargetWithTheQuery()
    {
        var document = Load("<iframe name=target></iframe><form action=result.html target=target><input name=a value='b c'><input name=é value=&amp;></form>");
        ((HtmlFormElement)document.QuerySelector("form")!).Submit();
        _contexts.EventLoop.RunUntilIdle();

        var request = LastDocumentRequest;
        Assert.Equal("https://example.com/dir/result.html?a=b+c&%C3%A9=%26", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Null(request.Body);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded", "application/x-www-form-urlencoded", "a=1&b=x%0D%0Ay")]
    [InlineData("text/plain", "text/plain", "a=1\r\nb=x\r\ny\r\n")]
    public void Post_SendsTheEncodedBody(string enctype, string contentType, string body)
    {
        var document = Load($"<iframe name=target></iframe><form method=post enctype={enctype} action=post target=target><input name=a value=1><textarea name=b>x\ny</textarea></form>");
        ((HtmlFormElement)document.QuerySelector("form")!).RequestSubmit();
        _contexts.EventLoop.RunUntilIdle();

        var request = LastDocumentRequest;
        Assert.Equal("POST", request.Method);
        Assert.Equal(contentType, request.BodyContentType);
        Assert.Equal(body, Encoding.UTF8.GetString(request.Body!.Value.Span));
    }

    [Fact]
    public void Post_Multipart()
    {
        var document = Load("<iframe name=target></iframe><form method=post enctype=multipart/form-data action=post target=target><input name='a\"b' value=1></form>");
        ((HtmlFormElement)document.QuerySelector("form")!).Submit();
        _contexts.EventLoop.RunUntilIdle();

        var request = LastDocumentRequest;
        Assert.StartsWith("multipart/form-data; boundary=", request.BodyContentType, StringComparison.Ordinal);
        var boundary = request.BodyContentType!["multipart/form-data; boundary=".Length..];
        Assert.Equal($"--{boundary}\r\nContent-Disposition: form-data; name=\"a%22b\"\r\n\r\n1\r\n--{boundary}--\r\n", Encoding.UTF8.GetString(request.Body!.Value.Span));
    }

    [Fact]
    public void Post_Multipart_FileInputWithoutSelectedFile()
    {
        var document = Load("<iframe name=target></iframe><form method=post enctype=multipart/form-data action=post target=target><input type=file name=f></form>");
        ((HtmlFormElement)document.QuerySelector("form")!).Submit();
        _contexts.EventLoop.RunUntilIdle();

        var request = LastDocumentRequest;
        var boundary = request.BodyContentType!["multipart/form-data; boundary=".Length..];
        Assert.Equal($"--{boundary}\r\nContent-Disposition: form-data; name=\"f\"; filename=\"\"\r\nContent-Type: application/octet-stream\r\n\r\n\r\n--{boundary}--\r\n", Encoding.UTF8.GetString(request.Body!.Value.Span));
    }

    [Fact]
    public void SubmitterAttributes_OverrideTheFormAttributes()
    {
        var document = Load("<iframe name=other></iframe><form action=a.html method=post><button formaction=b.html formmethod=get formtarget=other name=x value=y>submit</button></form>");
        ((HtmlFormElement)document.QuerySelector("form")!).RequestSubmit((HtmlElement)document.QuerySelector("button")!);
        _contexts.EventLoop.RunUntilIdle();

        var request = LastDocumentRequest;
        Assert.Equal("https://example.com/dir/b.html?x=y", request.Url);
        Assert.Equal("GET", request.Method);
    }

    [Fact]
    public void DialogMethod_ClosesTheDialogWithTheValueOfTheSubmitter()
    {
        var document = Load("<dialog open><form method=dialog><button value=ok>ok</button></form></dialog>");
        var dialog = (HtmlDialogElement)document.QuerySelector("dialog")!;
        ((HtmlElement)document.QuerySelector("button")!).Click();
        Assert.False(dialog.Open);
        Assert.Equal("ok", dialog.ReturnValue);
    }

    [Fact]
    public void DialogMethod_SubmitMethodClosesTheDialogWithAnEmptyReturnValue()
    {
        var document = Load("<dialog open><form method=dialog><input type=image alt=ok></form></dialog>");
        var dialog = (HtmlDialogElement)document.QuerySelector("dialog")!;
        dialog.ReturnValue = "initial";
        ((HtmlFormElement)document.QuerySelector("form")!).Submit();
        Assert.False(dialog.Open);
        Assert.Equal("", dialog.ReturnValue);

        // An image button activated without a pointing device selects the (0,0) coordinate
        dialog.Show();
        ((HtmlElement)document.QuerySelector("input")!).Click();
        Assert.False(dialog.Open);
        Assert.Equal("0,0", dialog.ReturnValue);
    }

    [Fact]
    public void SubmitEventCanceled_DoesNotNavigate()
    {
        var document = Load("<iframe name=target></iframe><form action=result.html target=target></form>");
        var form = (HtmlFormElement)document.QuerySelector("form")!;
        form.AddEventListener("submit", e => e.PreventDefault());
        var requests = _loader.Requests.Count;
        form.RequestSubmit();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(requests, _loader.Requests.Count);
    }

    [Fact]
    public void PlannedNavigation_IsReplacedByTheNextSubmission()
    {
        var document = Load("<iframe name=target></iframe><form action=result.html target=target><input name=a></form>");
        var form = (HtmlFormElement)document.QuerySelector("form")!;
        var input = (HtmlInputElement)document.QuerySelector("input")!;
        input.Value = "1";
        form.Submit();
        input.Value = "2";
        form.Submit();
        _contexts.EventLoop.RunUntilIdle();

        Assert.Single(_loader.Requests, r => r.Url.Contains("result.html", StringComparison.Ordinal));
        Assert.Equal("https://example.com/dir/result.html?a=2", LastDocumentRequest.Url);
    }

    [Fact]
    public void PlannedNavigation_IsNotReplacedBySubmitToAnotherTarget()
    {
        var document = Load("<iframe name=a></iframe><iframe name=b></iframe><form action=result.html><input name=a></form>");
        var form = (HtmlFormElement)document.QuerySelector("form")!;
        form.Target = "a";
        form.Submit();
        form.Target = "b";
        form.Submit();
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(2, _loader.Requests.Count(r => r.Url.Contains("result.html", StringComparison.Ordinal)));
    }

    [Fact]
    public void ImplicitSubmission_ClicksTheDefaultButton()
    {
        var document = Load("<iframe name=target></iframe><form action=result.html target=target><input name=q><button name=b value=1>go</button></form>");
        var input = (HtmlInputElement)document.QuerySelector("input")!;
        input.Value = "text";
        var keydown = new KeyboardEvent("keydown", new KeyboardEventInit { Key = "Enter", Bubbles = true, Cancelable = true }) { IsTrusted = true };
        EventDispatcher.Dispatch(keydown, input);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("https://example.com/dir/result.html?q=text&b=1", LastDocumentRequest.Url);
    }
}
