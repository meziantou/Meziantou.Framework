using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.ServiceDefaults;

internal sealed class ValidationStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            var options = app.ApplicationServices.GetService<MeziantouServiceDefaultsOptions>();
            if (!options.MapCalled)
                throw new InvalidOperationException($"You must call {nameof(MeziantouServiceDefaults.MapMeziantouDefaultEndpoints)}.");

            next(app);
        };
    }
}
