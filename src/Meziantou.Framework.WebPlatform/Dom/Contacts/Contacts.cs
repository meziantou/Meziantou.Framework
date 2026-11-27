using Meziantou.Framework.WebPlatform.Dom.FileApi;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A property of a contact (https://w3c.github.io/contact-picker/#enumdef-contactproperty).</summary>
/// <seealso href="https://w3c.github.io/contact-picker/#enumdef-contactproperty"/>
[IdlInterface("ContactProperty", "https://w3c.github.io/contact-picker/#enumdef-contactproperty")]
public enum ContactProperty
{
    /// <summary>The physical addresses.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactproperty-address"/>
    [IdlMember("address", "https://w3c.github.io/contact-picker/#dom-contactproperty-address")]
    Address,

    /// <summary>The email addresses.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactproperty-email"/>
    [IdlMember("email", "https://w3c.github.io/contact-picker/#dom-contactproperty-email")]
    Email,

    /// <summary>The icons (images).</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactproperty-icon"/>
    [IdlMember("icon", "https://w3c.github.io/contact-picker/#dom-contactproperty-icon")]
    Icon,

    /// <summary>The names.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactproperty-name"/>
    [IdlMember("name", "https://w3c.github.io/contact-picker/#dom-contactproperty-name")]
    Name,

    /// <summary>The phone numbers.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactproperty-tel"/>
    [IdlMember("tel", "https://w3c.github.io/contact-picker/#dom-contactproperty-tel")]
    Tel,
}

/// <summary>The options of <c>navigator.contacts.select()</c> (https://w3c.github.io/contact-picker/#dictdef-contactsselectoptions).</summary>
/// <seealso href="https://w3c.github.io/contact-picker/#dictdef-contactsselectoptions"/>
[IdlInterface("ContactsSelectOptions", "https://w3c.github.io/contact-picker/#dictdef-contactsselectoptions")]
public sealed class ContactsSelectOptions
{
    /// <summary>Gets whether the user can select several contacts.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactsselectoptions-multiple"/>
    [IdlMember("multiple", "https://w3c.github.io/contact-picker/#dom-contactsselectoptions-multiple")]
    public bool Multiple { get; init; }
}

/// <summary>
/// A contact returned by <c>navigator.contacts.select()</c> (https://w3c.github.io/contact-picker/#dictdef-contactinfo): the
/// requested properties are lists (empty when the contact has no value), the other ones are null.
/// </summary>
/// <seealso href="https://w3c.github.io/contact-picker/#dictdef-contactinfo"/>
[IdlInterface("ContactInfo", "https://w3c.github.io/contact-picker/#dictdef-contactinfo")]
public sealed class ContactInfo
{
    /// <summary>Gets the physical addresses, or null when they were not requested.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactinfo-address"/>
    [IdlMember("address", "https://w3c.github.io/contact-picker/#dom-contactinfo-address")]
    public IReadOnlyList<ContactAddress>? Address { get; init; }

    /// <summary>Gets the email addresses, or null when they were not requested.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactinfo-email"/>
    [IdlMember("email", "https://w3c.github.io/contact-picker/#dom-contactinfo-email")]
    public IReadOnlyList<string>? Email { get; init; }

    /// <summary>Gets the icons, or null when they were not requested.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactinfo-icon"/>
    [IdlMember("icon", "https://w3c.github.io/contact-picker/#dom-contactinfo-icon")]
    public IReadOnlyList<Blob>? Icon { get; init; }

    /// <summary>Gets the names, or null when they were not requested.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactinfo-name"/>
    [IdlMember("name", "https://w3c.github.io/contact-picker/#dom-contactinfo-name")]
    public IReadOnlyList<string>? Name { get; init; }

    /// <summary>Gets the phone numbers, or null when they were not requested.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactinfo-tel"/>
    [IdlMember("tel", "https://w3c.github.io/contact-picker/#dom-contactinfo-tel")]
    public IReadOnlyList<string>? Tel { get; init; }
}

/// <summary>
/// The Contact Picker API of a window (https://w3c.github.io/contact-picker/#contacts-manager, <c>navigator.contacts</c>):
/// the user selects contacts of the <see cref="ContactsProvider"/> of the browsing context (<see cref="BrowsingContextOptions.Contacts"/>).
/// </summary>
/// <seealso href="https://w3c.github.io/contact-picker/#contactsmanager"/>
[IdlInterface("ContactsManager", "https://w3c.github.io/contact-picker/#contactsmanager")]
public sealed class ContactsManager
{
    private readonly Window _window;

    // The contact picker is showing flag of the navigable (https://w3c.github.io/contact-picker/#contacts-picker-is-showing): the
    // manager is the one of the window of the top-level traversable
    private bool _isShowing;

    internal ContactsManager(Window window) => _window = window;

    private ContactsProvider? Provider => _window.Options.Contacts?.Provider;

    /// <summary>
    /// Returns the properties that the contacts source supports (https://w3c.github.io/contact-picker/#dom-contactsmanager-getproperties):
    /// <see cref="ContactsProvider.SupportedProperties"/>, or none when the browsing context has no provider.
    /// </summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactsmanager-getproperties"/>
    [IdlMember("getProperties", "https://w3c.github.io/contact-picker/#dom-contactsmanager-getproperties")]
    public Task<IReadOnlyList<ContactProperty>> GetProperties()
    {
        var result = new TaskCompletionSource<IReadOnlyList<ContactProperty>>();
        var properties = GetSupportedProperties();
        _window.QueueGlobalTask(() => result.TrySetResult(properties));
        return result.Task;
    }

    /// <summary>
    /// Asks the user to select contacts (https://w3c.github.io/contact-picker/#dom-contactsmanager-select) with
    /// <see cref="ContactsProvider.SelectAsync"/>, and returns the requested properties of the selected contacts (an empty list
    /// when the user cancels). The window must be the top-level window, and have transient activation, which is consumed.
    /// The task fails with an <c>InvalidStateError</c> <see cref="DomException"/> in a frame, while the picker is showing, or
    /// when the picker fails, a <c>SecurityError</c> without transient activation, and a <see cref="TypeErrorException"/> when
    /// there is no property or a property is not supported.
    /// </summary>
    /// <param name="properties">The properties to share.</param>
    /// <param name="options">The options: whether the user can select several contacts.</param>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactsmanager-select"/>
    [IdlMember("select", "https://w3c.github.io/contact-picker/#dom-contactsmanager-select")]
    public Task<IReadOnlyList<ContactInfo>> Select(IReadOnlyList<ContactProperty> properties, ContactsSelectOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        options ??= new ContactsSelectOptions();
        if (_window.BrowsingContext is not { Parent: null })
            return Task.FromException<IReadOnlyList<ContactInfo>>(DomException.InvalidState("Failed to execute 'select' on 'ContactsManager': The contacts API can only be used in the top frame."));

        if (!_window.HasTransientActivation)
            return Task.FromException<IReadOnlyList<ContactInfo>>(new DomException("Failed to execute 'select' on 'ContactsManager': A user gesture is required to call this method.", "SecurityError"));

        _window.ConsumeUserActivation();
        if (_isShowing)
            return Task.FromException<IReadOnlyList<ContactInfo>>(DomException.InvalidState("Failed to execute 'select' on 'ContactsManager': The contact picker is already showing."));

        if (properties.Count == 0)
            return Task.FromException<IReadOnlyList<ContactInfo>>(new TypeErrorException("Failed to execute 'select' on 'ContactsManager': At least one property must be provided."));

        var supported = GetSupportedProperties();
        foreach (var property in properties)
        {
            if (!supported.Contains(property))
                return Task.FromException<IReadOnlyList<ContactInfo>>(new TypeErrorException($"Failed to execute 'select' on 'ContactsManager': The property '{property.ToString().ToLowerInvariant()}' is not supported."));
        }

        _isShowing = true;
        var result = new TaskCompletionSource<IReadOnlyList<ContactInfo>>();
        var requested = properties.Distinct().ToArray();
        Task<IReadOnlyList<UserContact>> picker;
        try
        {
            picker = Provider!.SelectAsync(new ContactPickerRequest(CallbackContext.For(_window), _window.OriginRecord.Serialize(), requested, options.Multiple));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            picker = Task.FromException<IReadOnlyList<UserContact>>(exception);
        }

        _ = picker.ContinueWith(
            task => _window.QueueGlobalTask(() =>
            {
                _isShowing = false;
                if (!task.IsCompletedSuccessfully || task.Result is null)
                {
                    result.TrySetException(DomException.InvalidState("Failed to execute 'select' on 'ContactsManager': The contact picker failed."));
                    return;
                }

                var selected = options.Multiple ? task.Result : task.Result.Take(1);
                result.TrySetResult(selected.Select(contact => CreateContactInfo(contact, requested)).ToArray());
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    private ContactProperty[] GetSupportedProperties() => Provider is { } provider ? [.. provider.SupportedProperties.Distinct()] : [];

    // https://w3c.github.io/contact-picker/#dom-contactsmanager-select: the requested properties are the lists of the contact,
    // the other ones are undefined
    private static ContactInfo CreateContactInfo(UserContact contact, ContactProperty[] properties) => new()
    {
        Address = properties.Contains(ContactProperty.Address) ? contact.Addresses.Distinct().Select(CreateAddress).ToArray() : null,
        Email = properties.Contains(ContactProperty.Email) ? contact.Emails.Distinct(StringComparer.Ordinal).ToArray() : null,
        Icon = properties.Contains(ContactProperty.Icon) ? contact.Icons.Distinct().ToArray() : null,
        Name = properties.Contains(ContactProperty.Name) ? contact.Names.Distinct(StringComparer.Ordinal).ToArray() : null,
        Tel = properties.Contains(ContactProperty.Tel) ? contact.Numbers.Distinct(StringComparer.Ordinal).ToArray() : null,
    };

    // https://w3c.github.io/contact-picker/#contactaddress-from-user-provided-input: a new object for the page, whose country is
    // an uppercase code
    private static ContactAddress CreateAddress(ContactAddress address) => new()
    {
        City = address.City,
        Country = address.Country.ToUpperInvariant(),
        DependentLocality = address.DependentLocality,
        Organization = address.Organization,
        Phone = address.Phone,
        PostalCode = address.PostalCode,
        Recipient = address.Recipient,
        Region = address.Region,
        SortingCode = address.SortingCode,
        AddressLine = [.. address.AddressLine],
    };
}

/// <summary>The host hooks of the Contact Picker API (https://w3c.github.io/contact-picker/, <see cref="BrowsingContextOptions.Contacts"/>).</summary>
public sealed class ContactsOptions
{
    /// <summary>
    /// Gets or sets the contacts source of the user (https://w3c.github.io/contact-picker/#contacts-source): a
    /// <see cref="ContactsProvider"/> that plays the role of the contact picker. The default is null: no property is
    /// supported, so <c>navigator.contacts.select()</c> fails.
    /// </summary>
    public ContactsProvider? Provider { get; init; }
}

/// <summary>
/// The contacts source of the user (https://w3c.github.io/contact-picker/#contacts-source), shared by the windows of the
/// browsing contexts that share a <see cref="BrowsingContextOptions"/> (<see cref="ContactsOptions.Provider"/>): the
/// properties it supports, and <see cref="SelectAsync"/>, which plays the role of the contact picker.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class ContactsProvider
{
    /// <summary>
    /// Gets the properties that the source supports (the supported properties, https://w3c.github.io/contact-picker/#contacts-source-supported-properties):
    /// a page can only request them. The default implementation returns all the properties.
    /// </summary>
    public virtual IReadOnlyList<ContactProperty> SupportedProperties { get; } = [ContactProperty.Address, ContactProperty.Email, ContactProperty.Icon, ContactProperty.Name, ContactProperty.Tel];

    /// <summary>
    /// Shows the contact picker (https://w3c.github.io/contact-picker/#launch-a-contact-picker): the user selects contacts
    /// (one, unless <see cref="ContactPickerRequest.Multiple"/>) and chooses the information to share among
    /// <see cref="ContactPickerRequest.Properties"/>. Returns the selected contacts (an empty list when the user cancels; the
    /// properties that were not requested are ignored). When the task fails, or returns null, the request fails with an
    /// <c>InvalidStateError</c>. The default implementation returns an empty list.
    /// </summary>
    /// <param name="request">The request of the window.</param>
    public virtual Task<IReadOnlyList<UserContact>> SelectAsync(ContactPickerRequest request) => Task.FromResult<IReadOnlyList<UserContact>>([]);
}

/// <summary>A request of <c>navigator.contacts.select()</c>, given to <see cref="ContactsProvider.SelectAsync"/>.</summary>
public sealed class ContactPickerRequest
{
    internal ContactPickerRequest(CallbackContext context, string origin, IReadOnlyList<ContactProperty> properties, bool multiple)
    {
        Context = context;
        Origin = origin;
        Properties = properties;
        Multiple = multiple;
    }

    /// <summary>Gets the context of the window.</summary>
    public CallbackContext Context { get; }

    /// <summary>Gets the serialization of the origin of the window, which the picker shows.</summary>
    public string Origin { get; }

    /// <summary>Gets the requested properties.</summary>
    public IReadOnlyList<ContactProperty> Properties { get; }

    /// <summary>Gets whether the user can select several contacts.</summary>
    public bool Multiple { get; }
}

/// <summary>
/// A contact of the user (https://w3c.github.io/contact-picker/#user-contact): lists of names, email addresses, phone
/// numbers, addresses and icons, which need not have the same length.
/// </summary>
public sealed class UserContact
{
    /// <summary>Gets the names.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>Gets the email addresses.</summary>
    public IReadOnlyList<string> Emails { get; init; } = [];

    /// <summary>Gets the phone numbers.</summary>
    public IReadOnlyList<string> Numbers { get; init; } = [];

    /// <summary>Gets the physical addresses (the country is an ISO 3166-1 alpha-2 code).</summary>
    public IReadOnlyList<ContactAddress> Addresses { get; init; } = [];

    /// <summary>Gets the icons: images, whose type is an image MIME type.</summary>
    public IReadOnlyList<Blob> Icons { get; init; } = [];
}

// https://w3c.github.io/contact-picker/#extensions-to-navigator
public sealed partial class Navigator
{
    private ContactsManager? _contacts;

    /// <summary>Gets the Contact Picker API of the window.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-navigator-contacts"/>
    [IdlMember("contacts", "https://w3c.github.io/contact-picker/#dom-navigator-contacts")]
    public ContactsManager Contacts => _contacts ??= new ContactsManager(_window);
}
