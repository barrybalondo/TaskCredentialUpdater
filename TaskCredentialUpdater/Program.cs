using System.Text.Json;
using Serilog;
using TaskCredentialUpdater;
using TaskCredentialUpdater.Services;

using var logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    logger.Information("Starting credential update.");

    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("This job must run on Windows.");
    }

    logger.Information("Reading and validating appsettings.json.");
    var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    var settings = JsonSerializer.Deserialize<Settings>(
        File.ReadAllText(settingsPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (settings == null)
    {
        throw new InvalidOperationException("Unable to read appsettings.json.");
    }

    settings.Validate();

    // This client intentionally sends no authentication credentials.
    // The API is responsible for identifying the registered calling machine
    // and authorizing access to the requested credential.
    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30),
        // Limit buffered API responses to 16 KiB (16,384 bytes).
        MaxResponseContentBufferSize = 16 * 1024
    };

    var credentialClient = new CredentialApiClient(httpClient);
    logger.Information("Requesting the current password from the credential API.");
    var password = await credentialClient.GetPasswordAsync(settings.ApiUrl);

    logger.Information("Updating scheduled task {TaskPath}.", settings.TaskPath);
    ScheduledTaskUpdater.Update(settings.TaskPath, settings.AccountName, password);

    logger.Information("Successfully updated credentials for {TaskPath}.", settings.TaskPath);
}
catch (Exception exception)
{
    logger.Error(exception, "Credential update failed.");
    Environment.ExitCode = 1;
}
