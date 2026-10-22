using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskCredentialUpdater.Services;

public sealed class CredentialApiClient
{
    private readonly HttpClient _httpClient;

    public CredentialApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetPasswordAsync(string url)
    {
        // The configured URL includes any lookup parameters required by the API.
        using var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            // Never include credential API response bodies in exception messages or logs.
            throw new InvalidOperationException(
                $"Credential API returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        }

        try
        {
            var result = await response.Content.ReadFromJsonAsync<CredentialResponse>();

            if (string.IsNullOrEmpty(result?.Password))
            {
                throw new InvalidOperationException(
                    "Credential API returned an empty password.");
            }

            return result.Password;
        }
        catch (JsonException)
        {
            // Keep response content and JSON property paths out of exception logs.
            throw new InvalidOperationException(
                "Credential API returned invalid JSON. Expected an object with a password property.");
        }
    }
}

public sealed class CredentialResponse
{
    public string Password { get; }

    [JsonConstructor]
    public CredentialResponse(string password)
    {
        Password = password;
    }
}
