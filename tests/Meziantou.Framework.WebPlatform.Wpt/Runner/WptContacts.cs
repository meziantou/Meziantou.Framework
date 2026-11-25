using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The contacts source of the Contact Picker tests (<see cref="BrowsingContextOptions.Contacts"/>), driven by the
/// <c>WebContactsTest</c> class that the tests expect from the user agent (contacts/resources/helpers.js, the mock of
/// Chromium needs MojoJS): <c>setSelectedContacts()</c> sets the contacts that the picker returns, or null for a picker that
/// fails.
/// </summary>
internal sealed class WptContactsProvider : ContactsProvider
{
    private const string Script = """
        (() => {
          class WebContactsTest {
            setSelectedContacts(contacts) {
              if (contacts === null || contacts === undefined) {
                __wpt_contacts_set(null);
                return;
              }

              __wpt_contacts_set(JSON.stringify(contacts.map(contact => ({ name: contact.name, email: contact.email, tel: contact.tel, address: contact.address }))));
              contacts.forEach((contact, index) => (contact.icon || []).forEach(icon => __wpt_contacts_add_icon(index, icon)));
            }
          }

          Object.defineProperty(self, 'WebContactsTest', { value: WebContactsTest, writable: true, enumerable: false, configurable: true });
        })();
        """;

    private readonly Lock _lock = new();
    private List<(UserContact Contact, List<Blob> Icons)>? _contacts = [];

    public override Task<IReadOnlyList<UserContact>> SelectAsync(ContactPickerRequest request)
    {
        lock (_lock)
        {
            if (_contacts is null)
                return Task.FromResult<IReadOnlyList<UserContact>>(null!);

            return Task.FromResult<IReadOnlyList<UserContact>>([.. _contacts.Select(static entry => new UserContact
            {
                Names = entry.Contact.Names,
                Emails = entry.Contact.Emails,
                Numbers = entry.Contact.Numbers,
                Addresses = entry.Contact.Addresses,
                Icons = [.. entry.Icons],
            })]);
        }
    }

    public void DefineTestApi<TAdapter, TValue>(WebIdlRealm<TAdapter, TValue> realm)
        where TAdapter : struct, IJsAdapter<TValue>
        where TValue : class
    {
        var adapter = realm.Adapter;

        void Define(string name, int length, JsHostFunction<TValue> function)
            => adapter.DefineOwnProperty(realm.Global, adapter.FromString(name), JsPropertyDescriptor<TValue>.Data(adapter.CreateFunction(name, length, function), writable: true, enumerable: false, configurable: true));

        Define("__wpt_contacts_set", 1, (_, args) =>
        {
            lock (_lock)
            {
                _contacts = realm.IsNullOrUndefined(args[0]) ? null : ParseContacts(adapter.ToString(args[0]));
            }

            return adapter.Undefined;
        });
        Define("__wpt_contacts_add_icon", 2, (_, args) =>
        {
            var index = (int)adapter.ToNumber(args[0]);
            var icon = realm.ToInterface<Blob>(args[1], "Blob");
            lock (_lock)
            {
                if (_contacts is not null && index >= 0 && index < _contacts.Count)
                {
                    _contacts[index].Icons.Add(icon);
                }
            }

            return adapter.Undefined;
        });
        realm.EvaluateBuiltIn(Script);
    }

    private static List<(UserContact Contact, List<Blob> Icons)> ParseContacts(string json)
    {
        var result = new List<(UserContact, List<Blob>)>();
        foreach (var node in JsonNode.Parse(json)?.AsArray() ?? [])
        {
            var contact = new UserContact
            {
                Names = GetStrings(node?["name"]),
                Emails = GetStrings(node?["email"]),
                Numbers = GetStrings(node?["tel"]),
                Addresses = [.. (node?["address"]?.AsArray() ?? []).Select(static address => new ContactAddress
                {
                    City = address?["city"]?.GetValue<string>() ?? "",
                    Country = address?["country"]?.GetValue<string>() ?? "",
                    DependentLocality = address?["dependentLocality"]?.GetValue<string>() ?? "",
                    Organization = address?["organization"]?.GetValue<string>() ?? "",
                    Phone = address?["phone"]?.GetValue<string>() ?? "",
                    PostalCode = address?["postalCode"]?.GetValue<string>() ?? "",
                    Recipient = address?["recipient"]?.GetValue<string>() ?? "",
                    Region = address?["region"]?.GetValue<string>() ?? "",
                    SortingCode = address?["sortingCode"]?.GetValue<string>() ?? "",
                    AddressLine = GetStrings(address?["addressLine"]),
                })],
            };
            result.Add((contact, []));
        }

        return result;
    }

    private static string[] GetStrings(JsonNode? node) => node is JsonArray array ? [.. array.Select(static value => value?.GetValue<string>() ?? "")] : [];
}

/// <summary>The MIDI ports of the Web MIDI tests (<see cref="BrowsingContextOptions.Midi"/>): an output whose messages loop back to an input.</summary>
internal static class WptMidi
{
    public static MidiProvider CreateProvider()
    {
        var provider = new MidiProvider();
        var input = new MidiHostInput("wpt-input") { Name = "WPT Loopback", Manufacturer = "web-platform-tests", Version = "1.0" };
        provider.Connect(input);
        provider.Connect(new LoopbackOutput(input) { Name = "WPT Loopback", Manufacturer = "web-platform-tests", Version = "1.0" });
        return provider;
    }

    private sealed class LoopbackOutput(MidiHostInput input) : MidiHostOutput("wpt-output")
    {
        public override void Send(ReadOnlyMemory<byte> data, TimeSpan delay) => input.Receive(data.Span);
    }
}
