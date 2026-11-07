// The tests of the analyzer analyze the files of this folder with taggedvalues.infer_tags_from_names = true
namespace Sample.NamingConventions;

#region TaggedValues_NamingConventions
public sealed class Customer
{
    public Guid Id { get; set; }        // a CustomerId, from the name of the type
    public Guid AccountId { get; set; } // an AccountId, from the name of the property
}

public sealed class CustomerService
{
    public void Delete(Guid customerId) { } // a CustomerId, from the name of the parameter

    public void Archive(Customer id) { }    // a CustomerId, from the type of the parameter

    public void M(Customer customer)
    {
        Delete(customer.Id);        // ok
        Delete(customer.AccountId); // MFTV0002
    }
}
#endregion

#region TaggedValues_MFTV0007
public sealed class Invoice
{
    [ValueTag("CustomerId")] public Guid CustomerId { get; set; } // MFTV0007
}
#endregion
