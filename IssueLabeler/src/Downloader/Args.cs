// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

public struct Args
{
    public string Org { get; set; }
    public List<string> Repos { get; set; }
    public string GithubToken { get; set; }
    public string? IssuesDataPath { get; set; }
    public int? IssuesLimit { get; set; }
    public string? PullsDataPath { get; set; }
    public int? PullsLimit { get; set; }
    public int? PageSize { get; set; }
    public int? PageLimit { get; set; }
    public int[] Retries { get; set; }
    public string[]? ExcludedAuthors { get; set; }
    public Predicate<string> LabelPredicate { get; set; }
    public bool Verbose { get; set; }

    static void ShowUsage(string? message = null)
    {
        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Usage:
              {{executableName}} --repo {org/repo1}[,{org/repo2},...] --label-prefix {label-prefix} [options]

              Required environment variables:
                GITHUB_TOKEN      GitHub token to be used for API calls.

              Required arguments:
                --repo              The GitHub repositories in format org/repo (comma separated for multiple).
                --label-prefix      Prefix for label predictions. Must end with a character other than a letter or number.

              Required for downloading issue data:
                --issues-data       Path for issue data file to create (TSV file).

              Required for downloading pull request data:
                --pulls-data        Path for pull request data file to create (TSV file).

              Optional arguments:
                --issues-limit      Maximum number of issues to download.
                --pulls-limit       Maximum number of pull requests to download.
                --page-size         Number of items per page in GitHub API requests.
                --page-limit        Maximum number of pages to retrieve.
                --excluded-authors  Comma-separated list of authors to exclude.
                --retries           Comma-separated retry delays in seconds. Default: 30,30,300,300,3000,3000.
                --verbose           Enable verbose output.
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
            Retries = [30, 30, 300, 300, 3000, 3000]
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

                case "--pulls-limit":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--pulls-limit", out int? PullsLimit))
                    {
                        return null;
                    }
                    argsData.PullsLimit = PullsLimit;
                    break;

                case "--page-size":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--page-size", out int? pageSize))
                    {
                        return null;
                    }
                    argsData.PageSize = pageSize;
                    break;

                case "--page-limit":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--page-limit", out int? pageLimit))
                    {
                        return null;
                    }
                    argsData.PageLimit = pageLimit;
                    break;

                case "--excluded-authors":
                    if (!ArgUtils.TryDequeueStringArray(arguments, "--excluded-authors", out string[]? excludedAuthors))
                    {
                        return null;
                    }
                    argsData.ExcludedAuthors = excludedAuthors;
                    break;

                case "--retries":
                    if (!ArgUtils.TryDequeueIntArray(arguments, ShowUsage, "--retries", out int[]? retries))
                    {
                        return null;
                    }
                    argsData.Retries = retries;
                    break;

                case "--label-prefix":
                    if (!ArgUtils.TryDequeueLabelPrefix(arguments, ShowUsage, "--label-prefix", out Func<string, bool>? labelPredicate))
                    {
                        return null;
                    }
                    argsData.LabelPredicate = new(labelPredicate);
                    break;

                case "--verbose":
                    argsData.Verbose = true;
                    break;
                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (argsData.Org is null || argsData.Repos is null || argsData.LabelPredicate is null ||
            (argsData.IssuesDataPath is null && argsData.PullsDataPath is null))
        {
            ShowUsage();
            return null;
        }

        return argsData;
    }
}
