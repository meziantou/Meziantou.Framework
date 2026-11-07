namespace SampleApp.Domain;

public sealed record OrderLine(string Product, int Quantity, decimal Price);
