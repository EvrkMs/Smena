using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;

namespace Host.Services.Warehouse;

/// <summary>
/// Typed HTTP client for МойСклад API.
/// Pre-configured with the required Accept header and authorization
/// so consumers can call GetAsync without boilerplate.
/// </summary>
public sealed class SkladHttpClient
{
    private readonly HttpClient _http;

    public SkladHttpClient(HttpClient http, IOptions<WarehouseOptions> options)
    {
        var opts = options.Value;

        // МойСклад требует именно этот Accept — TryAddWithoutValidation нужен,
        // так как .NET отклоняет ;charset=utf-8 в MediaTypeWithQualityHeaderValue.
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json;charset=utf-8");

        switch (opts.AuthMode)
        {
            case WarehouseAuthMode.Token when !string.IsNullOrWhiteSpace(opts.Token):
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opts.Token.Trim());
                break;

            case WarehouseAuthMode.LoginPassword when !string.IsNullOrWhiteSpace(opts.Login):
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{opts.Login.Trim()}:{opts.Password?.Trim()}"));
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", credentials);
                break;
        }

        _http = http;
    }

    public Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct = default)
        => _http.GetAsync(url, ct);
}
