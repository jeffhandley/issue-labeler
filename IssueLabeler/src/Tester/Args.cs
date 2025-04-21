// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

public struct Args
{
    public string? Org { get; set; }
    public List<string> Repos { get; set; }
    public string? GithubToken { get; set; }
    public string? IssuesDataPath { get; set; }
    public string? IssuesModelPath { get; set; }
    public int? IssuesLimit { get; set; }
    public string? PullsDataPath { get; set; }
    public string? PullsModelPath { get; set; }
    public int? PullsLimit { get; set; }
    public float? Threshold { get; set; }
    public Predicate<string> LabelPredicate { get; set; }
    public string[]? ExcludedAuthors { get; set; }

    static void ShowUsage(string? message = null)
    {
        // The entire condition is used to determine if the configuration is invalid.
        // If any of the following are true, the configuration is considered invalid:
        // • The LabelPredicate is null.
        // • Both IssuesDataPath and PullsDataPath are null, and either Org, Repos, or GithubToken is null.
        // • Both IssuesModelPath and PullsModelPath are null.

        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Usage:
              {{executableName}} --repo {org/repo1}[,{org/repo2},...] --label-prefix {label-prefix} [options]

                Required environment variables:
                  GITHUB_TOKEN        GitHub token to be used for API calls.

                Required arguments:
                  --repo              The GitHub repositories in format org/repo (comma separated for multiple).
                  --label-prefix      Prefix for label predictions. Must end with a character other than a letter or number.

                Required for testing the issues model:
                  --issues-data       Path to existing issue data file (TSV file).
                  --issues-model      Path to existing issue prediction model file (ZIP file).

                Required for testing the pull requests model:
                  --pulls-data        Path to existing pull request data file (TSV file).
                  --pulls-model       Path to existing pull request prediction model file (ZIP file).

                Optional arguments:
                  --threshold         Minimum prediction confidence threshold. Range (0,1]. Default 0.4.
                  --issues-limit      Maximum number of issues to download. Default: No limit.
                  --pulls-limit       Maximum number of pull requests to download. Default: No limit.
                  --excluded-authors  Comma-separated list of authors to exclude.
            """);


        Environment.Exit(1);
    }

    public static Args? Parse(string[] args)
    {
        string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        if (string.IsNullOrEmpty(token))
        {
            ShowUsage("Environment variable GITHUB_TOKEN is empty.");
            return null;
        }

        Args argsData = new()
        {
            GithubToken = token,
            Threshold = 0.4f
        };

        Queue<string> arguments = new(args);
        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--repo":
                    if (!ArgUtils.TryDequeueRepoList(arguments, ShowUsage, "--repo", out string? org, out List<string>? repos))
                    {
                        return null;
                    }
                    argsData.Org = org;
                    argsData.Repos = repos;
                    break;

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

                case "--issues-limit":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--issues-limit", out int? IssuesLimit))
                    {
                        return null;
                    }
                    argsData.IssuesLimit = IssuesLimit;
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

                case "--pulls-limit":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--pulls-limit", out int? PullsLimit))
                    {
                        return null;
                    }
                    argsData.PullsLimit = PullsLimit;
                    break;

                case "--label-prefix":
                    if (!ArgUtils.TryDequeueLabelPrefix(arguments, ShowUsage, "--label-prefix", out Func<string, bool>? labelPredicate))
                    {
                        return null;
                    }
                    argsData.LabelPredicate = new(labelPredicate);
                    break;

                case "--threshold":
                    if (!ArgUtils.TryDequeueFloat(arguments, ShowUsage, "--threshold", out float? threshold))
                    {
                        return null;
                    }
                    argsData.Threshold = threshold.Value;
                    break;

                case "--excluded-authors":
                    if (!ArgUtils.TryDequeueStringArray(arguments, "--excluded-authors", out string[]? excludedAuthors))
                    {
                        return null;
                    }
                    argsData.ExcludedAuthors = excludedAuthors;
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (argsData.LabelPredicate is null ||
            (
                argsData.IssuesDataPath is null && argsData.PullsDataPath is null &&
                (argsData.Org is null || argsData.Repos.Count == 0 || argsData.GithubToken is null)
            ) ||
            (argsData.IssuesModelPath is null && argsData.PullsModelPath is null)
        )
        {
            ShowUsage();
            return null;
        }

        return argsData;
    }
}
