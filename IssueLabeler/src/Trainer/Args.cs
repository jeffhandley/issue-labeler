// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

public struct Args
{
    public string? IssuesDataPath { get; set; }
    public string? IssuesModelPath { get; set; }
    public string? PullsDataPath { get; set; }
    public string? PullsModelPath { get; set; }

    static void ShowUsage(string? message = null)
    {
        // If you provide a path for issue data, you must also provide a path for the issue model, and vice versa.
        // If you provide a path for pull data, you must also provide a path for the pull model, and vice versa.
        // At least one pair of paths(either issue or pull) must be provided.
        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Usage:
              {{executableName}} [options]

                Required for training the issues model:
                  --issues-data       Path to existing issue data file (TSV file).
                  --issues-model      Path for issue prediction model file to create (ZIP file).

                Required for training the pull requests model:
                  --pulls-data        Path to existing pull request data file (TSV file).
                  --pulls-model       Path for pull request prediction model file to create (ZIP file).
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args)
    {
        Args argsData = new();

        Queue<string> arguments = new(args);
        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--issues-data":
                    if (!ArgUtils.TryDequeuePath(arguments, "--issues-data", out string? IssuesDataPath))
                    {
                        return null;
                    }
                    argsData.IssuesDataPath = IssuesDataPath;
                    break;

                case "--issues-model":
                    if (!ArgUtils.TryDequeuePath(arguments, "--issues-model", out string? IssuesModelPath))
                    {
                        return null;
                    }
                    argsData.IssuesModelPath = IssuesModelPath;
                    break;

                case "--pulls-data":
                    if (!ArgUtils.TryDequeuePath(arguments, "--pulls-data", out string? PullsDataPath))
                    {
                        return null;
                    }
                    argsData.PullsDataPath = PullsDataPath;
                    break;

                case "--pulls-model":
                    if (!ArgUtils.TryDequeuePath(arguments, "--pulls-model", out string? PullsModelPath))
                    {
                        return null;
                    }
                    argsData.PullsModelPath = PullsModelPath;
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if ((argsData.IssuesDataPath is null != argsData.IssuesModelPath is null) ||
            (argsData.PullsDataPath is null != argsData.PullsModelPath is null) ||
            (argsData.IssuesModelPath is null && argsData.PullsModelPath is null))
        {
            ShowUsage();
            return null;
        }

        return argsData;
    }
}
