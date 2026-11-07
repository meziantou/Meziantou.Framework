namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#xmldocument</summary>
/// <seealso href="https://dom.spec.whatwg.org/#xmldocument"/>
[IdlInterface("XMLDocument", "https://dom.spec.whatwg.org/#xmldocument")]
public sealed class XmlDocument : Document
{
    internal XmlDocument(string contentType)
        : base(isHtmlDocument: false, contentType)
    {
    }

    internal override Node CloneSingleNode(Document document) => new XmlDocument(ContentType) { Encoding = Encoding, UrlRecord = UrlRecord, Mode = Mode, OriginDocument = GetOriginDocument(this), RealmDocument = this };
}
