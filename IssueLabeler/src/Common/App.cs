using Actions.Core.Markdown;
using Actions.Core.Services;

public static class App
{
    private async static Task<bool> RunTasks(Task allTasks, ICoreService action)
    {
        var success = false;

        try
        {
            allTasks.Wait();
            success = true;
        }
        catch (AggregateException ex)
        {
            action.WriteError($"Exception occurred: {ex.Message}");

            action.Summary.AddPersistent(summary =>
            {
                summary.AddAlert("Exception occurred", AlertType.Caution);
                summary.AddNewLine();
                summary.AddNewLine();
                summary.AddMarkdownCodeBlock(ex.Message);
            });
        }

        await action.Summary.WritePersistentAsync();
        return success;
    }

    public async static Task<(TResult[], bool)> RunTasks<TResult>(List<Task<TResult>> tasks, ICoreService action)
    {
        var allTasks = Task.WhenAll(tasks);
        var success = await RunTasks(allTasks, action);

        return (allTasks.Result, success);
    }

    public async static Task<bool> RunTasks(List<Task> tasks, ICoreService action)
    {
        var allTasks = Task.WhenAll(tasks);
        var success = await RunTasks(allTasks, action);

        return success;
    }
}
