using Microsoft.Extensions.DependencyInjection;


namespace Meziantou.AspNetCore.Components;

public static class ClipboardServiceExtensions
{
    public static IServiceCollection AddClipboard(this IServiceCollection serviceCollection)
    {
        return serviceCollection.AddScoped<ClipboardService>();
    }
}
