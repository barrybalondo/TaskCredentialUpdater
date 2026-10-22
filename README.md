# TaskCredentialUpdater — Job 1

A small Windows-only .NET 8 console application that retrieves the current service-account password from an HTTPS API, then updates the stored password for an *existing* Windows Scheduled Task (Job 2). It does **not** change the Active Directory account password itself.

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

No service-account password, machine certificate, or certificate thumbprint is stored or loaded by the console application.

`ApiUrl` is the complete HTTPS endpoint, including any non-secret query parameters. There is no separate `CredentialKey` setting; the example URL includes that lookup name in the query string. The client uses the configured URL without constructing or interpreting its query parameters.

Use the same account-name format for `AccountName` as the existing task reports. The comparison ignores capitalization but compares the text directly.

## API contract

The API will be implemented separately. The current provisional contract uses an HTTPS GET without a request body or added authentication credentials. The API is responsible for identifying the calling machine, checking its registration, and authorizing access to the requested credential before returning a password.

```http
GET /api/task-credentials/resolve?credentialKey=machine-checker
```

Expected authorized response:

```json
{"password":"<newly rotated password>"}
```

Return `Cache-Control: no-store` on the API response so HTTP caches do not store it. The endpoint returns the current password; it does not rotate the account password.

The request sends no machine name, registration identifier, token, or client certificate. The API or its hosting infrastructure must obtain the caller's machine identity from a trusted source and match it to the registration. A registration record or caller-supplied machine name alone does not prove which machine made the request. The mechanism for establishing that identity belongs to the API deployment and is outside this client project's scope. Running Job 1 with S4U does not automatically authenticate HTTP requests as the computer account.

For an unrecognized machine or a machine without permission for the requested credential, return `401` or `403` without a password. The client treats non-success responses as failures and does not update the scheduled task.

## Task Scheduler setup

Job 1 (S1) and Job 2 (S2) use the **same domain service account** with different logon modes. S1 and S2 describe the two task configurations, not different accounts.

1. Job 2 (`MachineChecker`) already runs as the designated domain service account using a **stored password** (`TaskLogonType.Password`), with network permissions.
2. Job 1 runs as the same account but with **Do not store password** (S4U). For the intended local-administrator setup, enable **Run with highest privileges** so the updater has effective administrative permissions to update Job 2. The target task's permissions must allow the update.
3. The Job 1 action launches `TaskCredentialUpdater.exe` from the published directory. Schedule it after rotation or periodically to reconcile the task password.
4. Verify that Job 2 executes successfully after credential update. Avoid overlapping runs and plan for failures during rotation.

Updating Job 2 uses the local Task Scheduler service. It does not require Job 1 to authenticate to another computer or know Job 2's previous stored password. The updater supplies the newly retrieved password when re-registering Job 2 for password logon. Job 1's ability to reach the credential API is a separate operation to verify under its actual S4U execution context.

The updater combines `TaskCreation.Update` with `TaskCreation.IgnoreRegistrationTriggers` to suppress runs caused by re-registration. Existing scheduled triggers remain active, so Job 2 can still run at its normal scheduled time. Verify this behavior on Windows with a harmless test task containing a registration trigger before deploying to Job 2.

## Security notes

- The API must verify the calling machine against its registration and authorize access to the requested credential. This client deliberately does not add authentication credentials.
- Never store or log the returned password or raw API response. Use trusted TLS and `Cache-Control: no-store` on API responses.
- Current updater validates that the target task is under the expected account and uses password logon, then re-registers it with `TaskCreation.Update | TaskCreation.IgnoreRegistrationTriggers` to preserve its schedule/actions/settings and suppress registration-trigger runs.
- The account used here may be an administrator on multiple computers; securing the API and machine is critical.
- This is sample code and has not been compiled or tested against Windows Task Scheduler in this environment.

## Logging

Logging uses Serilog with the console sink only, using its default settings. Log events go to standard output. Scheduled-task runs need their console output captured by the execution environment to retain a log history.

Progress entries record startup, configuration loading, the API request, the local task update, and successful completion. The last progress entry shows which step was underway when an error occurred; there is no separate operation-tracking variable.

Errors include the normal exception message, inner exceptions, and stack trace when available. Passwords, credential response objects, and API response bodies are not passed as logging arguments. Invalid API JSON is reported with a controlled message without carrying response property paths into the log.

Task paths use Serilog's structured `TaskPath` property instead of string interpolation. To integrate Seq later, add `Serilog.Sinks.Seq` and configure `WriteTo.Seq(...)` alongside the console sink; the existing logging calls can stay. The logger is disposed at the end of the run. A failed credential update exits with code `1`.

## Coding convention

Use `var` for local variables, including `using` declarations and `out` variables.

Use traditional `if` statements for null checks instead of `?? throw` expressions.

`Settings` and `CredentialResponse` use constructor initialization and get-only properties, with a blank line between each public property. JSON deserialization uses `[JsonConstructor]`; no mutable DTO properties are required.

## Finding the task path

In Task Scheduler, find the task under **Task Scheduler Library** and combine its **Location** with its task name. For example, a task named `MachineChecker` in `\\Operations` has full path `\\Operations\\MachineChecker` (shown with escaped backslashes in JSON). Alternatively, run:

```powershell
Get-ScheduledTask -TaskName 'MachineChecker' | Select-Object TaskPath, TaskName
```

Join the returned folder `TaskPath` and `TaskName` to obtain the full path used by `TaskService.GetTask`.
