// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Actions.Core.Summaries;

namespace Actions.Core.Services;

public static class GitHubActionSummary
{
    private static List<Action<Summary>> persistentSummaryWrites = [];

    public static void AddPersistent(this Summary summary, Action<Summary> writeToSummary)
    {
        persistentSummaryWrites.Add(writeToSummary);
        writeToSummary(summary);
    }

    public static async Task WriteStatusAsync(this ICoreService action, string message)
    {
        action.WriteInfo(message);

        await action.Summary.WritePersistentAsync(summary =>
        {
            summary.AddMarkdownHeading("Status", 3);
            summary.AddRaw(message);

            if (persistentSummaryWrites.Any())
            {
                summary.AddMarkdownHeading("Results", 3);
            }
        });
    }

    public static async Task WritePersistentAsync(this Summary summary, Action<Summary>? writeStatus = null)
    {
        await summary.ClearAsync();

        if (writeStatus is not null)
        {
            writeStatus(summary);
        }

        foreach (var write in persistentSummaryWrites)
        {
            write(summary);
        }

        await summary.WriteAsync();
    }
}
