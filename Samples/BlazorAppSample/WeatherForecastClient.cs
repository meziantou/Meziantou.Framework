using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace BlazorAppSample
{
    public class WeatherForecastClient
    {
        private readonly HttpClient _httpClient;

        public WeatherForecastClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IReadOnlyList<WeatherForecast>> GetAsync()
        {
            return await _httpClient.GetFromJsonAsync<WeatherForecast[]>("sample-data/weather.json");
        }
    }
}
