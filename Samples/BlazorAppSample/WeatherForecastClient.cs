using System.Net.Http.Json;

namespace BlazorAppSample
{
    public class WeatherForecastClient
    {
        private readonly HttpClient _httpClient;

        public WeatherForecastClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IReadOnlyList<WeatherForecast>> GetAsync(CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetFromJsonAsync<WeatherForecast[]>("sample-data/weather.json", cancellationToken).ConfigureAwait(false);
        }
    }
}
