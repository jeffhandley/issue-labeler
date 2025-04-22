using Actions.Core.Services;
using Actions.Core.Markdown;

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

            action.Summary.AddAlert("Exception occurred", AlertType.Caution);
            action.Summary.AddNewLine();
            action.Summary.AddNewLine();
            action.Summary.AddMarkdownCodeBlock(ex.Message);
        }

        await action.Summary.WriteAsync();
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
