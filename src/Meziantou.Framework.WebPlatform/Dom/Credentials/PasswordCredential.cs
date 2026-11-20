using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The data of a credential (https://w3c.github.io/webappsec-credential-management/#dictdef-credentialdata).</summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#dictdef-credentialdata"/>
[IdlInterface("CredentialData", "https://w3c.github.io/webappsec-credential-management/#dictdef-credentialdata")]
public abstract class CredentialData
{
    /// <summary>Gets the identifier of the credential (e.g. a user name).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialdata-id"/>
    [IdlMember("id", "https://w3c.github.io/webappsec-credential-management/#dom-credentialdata-id")]
    public required string Id { get; init; }
}

/// <summary>The data of a <see cref="PasswordCredential"/> (https://w3c.github.io/webappsec-credential-management/#dictdef-passwordcredentialdata).</summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#dictdef-passwordcredentialdata"/>
[IdlInterface("PasswordCredentialData", "https://w3c.github.io/webappsec-credential-management/#dictdef-passwordcredentialdata")]
public sealed class PasswordCredentialData : CredentialData
{
    /// <summary>Gets the name of the account, displayed by the credential chooser.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-passwordcredentialdata-name"/>
    [IdlMember("name", "https://w3c.github.io/webappsec-credential-management/#dom-passwordcredentialdata-name")]
    public string? Name { get; init; }

    /// <summary>Gets the URL of the image of the account, displayed by the credential chooser.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-passwordcredentialdata-iconurl"/>
    [IdlMember("iconURL", "https://w3c.github.io/webappsec-credential-management/#dom-passwordcredentialdata-iconurl")]
    public string? IconUrl { get; init; }

    /// <summary>Gets the serialization of the origin of the credential (e.g. <c>https://example.com</c>).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-passwordcredentialdata-origin"/>
    [IdlMember("origin", "https://w3c.github.io/webappsec-credential-management/#dom-passwordcredentialdata-origin")]
    public required string Origin { get; init; }

    /// <summary>Gets the password.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-passwordcredentialdata-password"/>
    [IdlMember("password", "https://w3c.github.io/webappsec-credential-management/#dom-passwordcredentialdata-password")]
    public required string Password { get; init; }
}

/// <summary>
/// A user name and password credential (https://w3c.github.io/webappsec-credential-management/#passwordcredential-interface),
/// stored by <c>navigator.credentials.store()</c> and returned by <c>navigator.credentials.get({ password: true })</c>. The
/// credential store of the user agent is the one of the <see cref="UserAgentUIBackend"/>: see
/// <see cref="UserAgentUIBackend.ChooseCredentialAsync"/> and <see cref="UserAgentUIBackend.StoreCredentialAsync"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#passwordcredential"/>
[IdlInterface("PasswordCredential", "https://w3c.github.io/webappsec-credential-management/#passwordcredential")]
public sealed class PasswordCredential : Credential
{
    /// <summary>
    /// Creates a credential from its data (https://w3c.github.io/webappsec-credential-management/#construct-passwordcredential-data).
    /// Throws a <see cref="TypeErrorException"/> when the identifier, the origin or the password is empty.
    /// </summary>
    /// <param name="data">The data of the credential.</param>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-passwordcredential-passwordcredential-data"/>
    [IdlMember("constructor", "https://w3c.github.io/webappsec-credential-management/#dom-passwordcredential-passwordcredential-data")]
    public PasswordCredential(PasswordCredentialData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Id.Length == 0 || data.Origin.Length == 0 || data.Password.Length == 0)
            throw new TypeErrorException("The id, the origin and the password of a PasswordCredential cannot be empty");

        Id = data.Id;
        Password = data.Password;
        Name = data.Name ?? "";
        IconUrl = data.IconUrl ?? "";
        Origin = data.Origin;
    }

    /// <summary>
    /// Creates a credential from the fields of a form (https://w3c.github.io/webappsec-credential-management/#construct-passwordcredential-form):
    /// the values of its submittable elements whose autofill field name is <c>username</c> (the identifier),
    /// <c>current-password</c> or <c>new-password</c> (the password, a new password winning), <c>name</c> or <c>nickname</c>
    /// (the name) and <c>photo</c> (the icon URL). The origin is the origin of the document of the current global object.
    /// Throws a <see cref="TypeErrorException"/> when the identifier or the password is empty.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-passwordcredential-passwordcredential"/>
    [IdlMember("constructor", "https://w3c.github.io/webappsec-credential-management/#dom-passwordcredential-passwordcredential")]
    public PasswordCredential(HtmlFormElement form)
        : this(FromForm(form, (Document.CurrentGlobalDocumentOrNull ?? form.NodeDocument).Origin.Serialize()))
    {
    }

    /// <inheritdoc/>
    public override string Id { get; }

    /// <inheritdoc/>
    public override string Type => "password";

    /// <summary>Gets the password.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-passwordcredential-password"/>
    [IdlMember("password", "https://w3c.github.io/webappsec-credential-management/#dom-passwordcredential-password")]
    public string Password { get; }

    /// <summary>Gets the name of the account (the empty string when there is none).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialuserdata-name"/>
    [IdlMember("name", "https://w3c.github.io/webappsec-credential-management/#dom-credentialuserdata-name")]
    public string Name { get; }

    /// <summary>Gets the URL of the image of the account (the empty string when there is none).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialuserdata-iconurl"/>
    [IdlMember("iconURL", "https://w3c.github.io/webappsec-credential-management/#dom-credentialuserdata-iconurl")]
    public string IconUrl { get; }

    /// <summary>Gets the serialization of the origin of the credential ([[origin]]): the site that the credential signs in to.</summary>
    [NotInIdl("The [[origin]] internal slot of the credential, which the scripts cannot read")]
    public string Origin { get; }

    /// <summary>https://w3c.github.io/webappsec-credential-management/#abstract-opdef-create-a-passwordcredential-from-an-htmlformelement</summary>
    internal static PasswordCredentialData FromForm(HtmlFormElement form, string origin)
    {
        ArgumentNullException.ThrowIfNull(form);
        var entries = form.ConstructEntryList(submitter: null) ?? throw DomException.InvalidState("The entry list of the form is being constructed");
        string? id = null;
        string? password = null;
        string? name = null;
        string? iconUrl = null;
        var newPasswordObserved = false;
        foreach (var node in form.GetRootNode().Descendants())
        {
            if (node is not HtmlElement { IsSubmittable: true } element || !ReferenceEquals(element.FormOwner, form) || !element.HasAttribute("autocomplete"))
                continue;

            if (element.GetAttributeValue("name") is not { Length: > 0 } fieldName || entries.FindIndex(entry => entry.Key == fieldName) is var index && index < 0)
                continue;

            if (entries[index].Value.StringValue is not { } value)
                continue;

            switch (Reflection.GetIdlExposedAutofillValue(element).Split(' ')[^1])
            {
                case "new-password":
                    password = value;
                    newPasswordObserved = true;
                    break;
                case "current-password":
                    if (!newPasswordObserved)
                    {
                        password = value;
                    }

                    break;
                case "photo":
                    iconUrl = value;
                    break;
                case "name" or "nickname":
                    name = value;
                    break;
                case "username":
                    id = value;
                    break;
            }
        }

        return new PasswordCredentialData { Id = id ?? "", Password = password ?? "", Name = name, IconUrl = iconUrl, Origin = origin };
    }
}
