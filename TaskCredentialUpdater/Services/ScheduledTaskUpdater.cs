using Microsoft.Win32.TaskScheduler;

namespace TaskCredentialUpdater.Services;

public static class ScheduledTaskUpdater
{
    public static void Update(string taskPath, string accountName, string password)
    {
        using var service = new TaskService();
        using var task = service.GetTask(taskPath);

        if (task == null)
        {
            throw new InvalidOperationException($"Scheduled task '{taskPath}' was not found.");
        }

        using var definition = task.Definition;

        // Do not accidentally change the account used by the existing task.
        if (!string.Equals(definition.Principal.UserId, accountName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The task's run-as account does not match configuration.");
        }

        if (definition.Principal.LogonType != TaskLogonType.Password)
        {
            throw new InvalidOperationException("The target task is not configured for password logon.");
        }

        // Updates the existing task. Its triggers, actions and settings are retained.
        // Suppress registration triggers so this credential update does not launch the task.
        using var updatedTask = task.Folder.RegisterTaskDefinition(
            task.Name,
            definition,
            TaskCreation.Update | TaskCreation.IgnoreRegistrationTriggers,
            accountName,
            password,
            TaskLogonType.Password);
    }
}
