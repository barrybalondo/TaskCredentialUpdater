using System.Text.Json.Serialization;

namespace TaskCredentialUpdater;

public sealed class Settings
{
    public string ApiUrl { get; }

    public string TaskPath { get; }

    public string AccountName { get; }

    [JsonConstructor]
    public Settings(
        string apiUrl,
        string taskPath,
        string accountName)
    {
        ApiUrl = apiUrl;
        TaskPath = taskPath;
        AccountName = accountName;
    }

    public void Validate()
    {
        if (!Uri.TryCreate(
                ApiUrl,
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0)
        {
            throw new InvalidOperationException(
                "ApiUrl must be a valid HTTPS URL without embedded credentials.");
        }

        if (string.IsNullOrWhiteSpace(TaskPath) ||
            string.IsNullOrWhiteSpace(AccountName))
        {
            throw new InvalidOperationException(
                "Required configuration is missing.");
        }
    }
}
