// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

public struct Args
{
    public string Org { get; set; }
    public List<string> Repos { get; set; }
    public string GithubToken { get; set; }
    public string? IssueDataPath { get; set; }
    public int? IssueLimit { get; set; }
    public string? PullDataPath { get; set; }
    public int? PullLimit { get; set; }
    public int? PageSize { get; set; }
    public int? PageLimit { get; set; }
    public int[] Retries { get; set; }
    public Predicate<string> LabelPredicate { get; set; }
    public bool Verbose { get; set; }

    static void ShowUsage(string? message = null)
    {
        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Usage:
              {{executableName}} --repo {org/repo1}[,{org/repo2},...] --label-prefix {label-prefix} [options]

              Required arguments:
                --repo              The GitHub repositories in format org/repo (comma separated for multiple).
                --label-prefix      Prefix for label predictions.

              Required for downloading issue data:
                --issue-data        Path for issue data file to create (TSV file).

              Required for downloading pull request data:
                --pull-data         Path for pull request data file to create (TSV file).

              Optional arguments:
                --issue-limit       Maximum number of issues to download.
                --pull-limit        Maximum number of pull requests to download.
                --page-size         Number of items per page in GitHub API requests.
                --page-limit        Maximum number of pages to retrieve.
                --retries           Comma-separated retry delays in seconds. Default: 30,30,300,300,3000,3000.
                --token             GitHub access token. Default: Read from GITHUB_TOKEN env var.
                --verbose           Enable verbose output.
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args)
    {
        Args config = new()
        {
            Retries = [30, 30, 300, 300, 3000, 3000]
        };

        Queue<string> arguments = new(args);
        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--token":
                    if (!ArgUtils.TryDequeueString(arguments, ShowUsage, "--token", out string? token))
                    {
                        return null;
                    }
                    config.GithubToken = token;
                    break;

                case "--repo":
                    if (!ArgUtils.TryDequeueRepoList(arguments, ShowUsage, "--repo", out string? org, out List<string>? repos))
                    {
                        return null;
                    }
                    config.Org = org;
                    config.Repos = repos;
                    break;

                case "--issue-data":
                    if (!ArgUtils.TryDequeuePath(arguments, ShowUsage, "--issue-data", out string? issueDataPath))
                    {
                        return null;
                    }
                    config.IssueDataPath = issueDataPath;
                    break;

                case "--issue-limit":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--issue-limit", out int? issueLimit))
                    {
                        return null;
                    }
                    config.IssueLimit = issueLimit;
                    break;

                case "--pull-data":
                    if (!ArgUtils.TryDequeuePath(arguments, ShowUsage, "--pull-data", out string? pullDataPath))
                    {
                        return null;
                    }
                    config.PullDataPath = pullDataPath;
                    break;

                case "--pull-limit":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--pull-limit", out int? pullLimit))
                    {
                        return null;
                    }
                    config.PullLimit = pullLimit;
                    break;

                case "--page-size":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--page-size", out int? pageSize))
                    {
                        return null;
                    }
                    config.PageSize = pageSize;
                    break;

                case "--page-limit":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--page-limit", out int? pageLimit))
                    {
                        return null;
                    }
                    config.PageLimit = pageLimit;
                    break;

                case "--retries":
                    if (!ArgUtils.TryDequeueIntArray(arguments, ShowUsage, "--retries", out int[]? retries))
                    {
                        return null;
                    }
                    config.Retries = retries;
                    break;

                case "--label-prefix":
                    if (!ArgUtils.TryDequeueLabelPrefix(arguments, ShowUsage, "--label-prefix", out Func<string, bool>? labelPredicate))
                    {
                        return null;
                    }
                    config.LabelPredicate = new(labelPredicate);
                    break;

                case "--verbose":
                    config.Verbose = true;
                    break;
                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (config.Org is null || config.Repos is null || config.LabelPredicate is null ||
            (config.IssueDataPath is null && config.PullDataPath is null))
        {
            ShowUsage();
            return null;
        }

        if (config.GithubToken is null)
        {
            string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

            if (string.IsNullOrEmpty(token))
            {
                ShowUsage("Argument '--token' not specified and environment variable GITHUB_TOKEN is empty.");
                return null;
            }

            config.GithubToken = token;
        }

        return config;
    }
}
