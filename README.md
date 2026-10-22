# TaskCredentialUpdater

A small Windows-only .NET 8 console application that retrieves the current account password from an HTTPS API and updates the stored credentials for an **existing local Windows Scheduled Task**. The target task is selected through configuration. Its actions, triggers, and settings are retained.

The main execution requirement is that the updater runs **without a stored logon password**, using an account with **local administrator rights** and **Run with highest privileges** enabled. Configure its scheduled task with **Do not store password** (S4U). These are Task Scheduler deployment settings; the application does not configure its own logon mode or elevate itself.

The retrieved password is supplied to the local Task Scheduler service for the target task. The application does not change the account password itself.

## Open in Visual Studio

Open `TaskCredentialUpdater.sln` on Windows with .NET 8 installed, restore packages and build. Uses the `TaskScheduler`, `Serilog`, and `Serilog.Sinks.Console` NuGet packages. Running the application and testing Task Scheduler updates require Windows.

## Configuration

Edit `TaskCredentialUpdater/appsettings.json`:

```json
{
  "ApiUrl": "https://credentials.example.com/api/task-credentials/resolve?credentialKey=machine-checker",
  "TaskPath": "\\Operations\\MachineChecker",
  "AccountName": "CONTOSO\\svc_machinecheck"
}
```

No account password is stored in configuration. The application retrieves it from the API during each run.

`ApiUrl` is the complete HTTPS endpoint, including any non-secret query parameters. There is no separate `CredentialKey` setting; the example URL includes that lookup name in the query string. The client uses the configured URL without constructing or interpreting its query parameters.

Use the same account-name format for `AccountName` as the existing task reports. The comparison ignores capitalization but compares the text directly.

## API integration

The API design is still in progress. The client currently makes an HTTPS GET without a request body or added authentication credentials. The API handles machine registration and authorization separately.

```http
GET /api/task-credentials/resolve?credentialKey=machine-checker
```

The response format and field names have **not been decided**. The current `CredentialApiClient` temporarily parses a JSON object with a `password` property. That is a placeholder in the client skeleton, not an agreed API contract. Update the response parsing once the API response is defined.

Credential responses should use `Cache-Control: no-store` so HTTP caches do not store them.

The client treats non-success HTTP responses as failures and does not update the scheduled task.

## Task Scheduler setup

1. Identify the existing local task whose stored credentials need updating. It must use **password logon** (`TaskLogonType.Password`). Set `TaskPath` to its full path and `AccountName` to its existing run-as account.
2. Create a scheduled task to launch `TaskCredentialUpdater.exe` from the published directory. Select **Run whether user is logged on or not** and **Do not store password** (S4U).
3. Run the updater under an account with **local administrator rights** and select **Run with highest privileges**. The target task's permissions must allow the update.
4. Schedule the updater after password rotation or periodically to refresh the target task's stored credentials. Verify the update and the target task's next run on Windows.

The updater uses the local Task Scheduler service and supplies the newly retrieved password when re-registering the target task. It does not need the target task's previous stored password.

The updater combines `TaskCreation.Update` with `TaskCreation.IgnoreRegistrationTriggers` to suppress runs caused by re-registration. Existing scheduled triggers remain active, so the target task can still run at its normal scheduled time. Verify this behavior on Windows with a harmless test task containing a registration trigger before deployment.

## Security notes

- The API must verify the calling machine against its registration and authorize access to the requested credential. This client deliberately does not add authentication credentials.
- Never store or log the returned password or raw API response. Use trusted TLS and `Cache-Control: no-store` on API responses.
- Current updater validates that the target task is under the expected account and uses password logon, then re-registers it with `TaskCreation.Update | TaskCreation.IgnoreRegistrationTriggers` to preserve its schedule/actions/settings and suppress registration-trigger runs.
- This is sample code and has not been compiled or tested against Windows Task Scheduler in this environment.

## Logging

Logging uses Serilog with the console sink only, using its default settings. Log events go to standard output. Scheduled-task runs need their console output captured by the execution environment to retain a log history.

Progress entries record startup, configuration loading, the API request, the local task update, and successful completion. The last progress entry shows which step was underway when an error occurred; there is no separate operation-tracking variable.

Errors include the normal exception message, inner exceptions, and stack trace when available. Passwords, credential response objects, and API response bodies are not passed as logging arguments. Invalid API JSON is reported with a controlled message without carrying response property paths into the log.

Task paths use Serilog's structured `TaskPath` property instead of string interpolation. To integrate Seq later, add `Serilog.Sinks.Seq` and configure `WriteTo.Seq(...)` alongside the console sink; the existing logging calls can stay. The logger is disposed at the end of the run. A failed credential update exits with code `1`.

## Finding the task path

In Task Scheduler, find the task under **Task Scheduler Library** and combine its **Location** with its task name. For example, a task named `MachineChecker` in `\\Operations` has full path `\\Operations\\MachineChecker` (shown with escaped backslashes in JSON). Alternatively, run:

```powershell
Get-ScheduledTask -TaskName 'MachineChecker' | Select-Object TaskPath, TaskName
```

Join the returned folder `TaskPath` and `TaskName` to obtain the full path used by `TaskService.GetTask`.
