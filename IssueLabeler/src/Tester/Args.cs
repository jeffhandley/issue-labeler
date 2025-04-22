// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Actions.Core.Services;

public struct Args
{
    public string GitHubToken => Environment.GetEnvironmentVariable("GITHUB_TOKEN")!;
    public string Org { get; set; }
    public List<string> Repos { get; set; }
    public float Threshold { get; set; }
    public Predicate<string> LabelPredicate { get; set; }
    public string[]? ExcludedAuthors { get; set; }
    public string? IssuesModelPath { get; set; }
    public int? IssuesLimit { get; set; }
    public string? PullsModelPath { get; set; }
    public int? PullsLimit { get; set; }
    public int? PageSize { get; set; }
    public int? PageLimit { get; set; }
    public int[] Retries { get; set; }
    public bool Verbose { get; set; }

    static void ShowUsage(string? message, ICoreService action)
    {
        action.WriteNotice($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Required environment variables:
              GITHUB_TOKEN            GitHub token to be used for API calls.

            Required arguments:
              --repo                  The GitHub repositories in format org/repo (comma separated for multiple).
              --label-prefix          Prefix for label predictions. Must end with a character other than a letter or number.

            Required for testing the issues model:
              --issues-model          Path to existing issue prediction model file (ZIP file).

            Required for testing the pull requests model:
              --pulls-model           Path to existing pull request prediction model file (ZIP file).

            Optional arguments:
              --excluded-authors      Comma-separated list of authors to exclude.
              --threshold             Minimum prediction confidence threshold. Range (0,1].
                                      Defaults to: 0.4.
              --issues-limit          Maximum number of issues to download. Defaults to: No limit.
              --pulls-limit           Maximum number of pull requests to download. Defaults to: No limit.
              --page-size             Number of items per page in GitHub API requests.
                                      Defaults to: 100 for issues, 25 for pull requests.
              --page-limit            Maximum number of pages to retrieve.
                                      Defaults to: 1000 for issues, 4000 for pull requests.
              --retries               Comma-separated retry delays in seconds.
                                      Defaults to: 30,30,300,300,3000,3000.
              --verbose               Enable verbose output.
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args, ICoreService action)
    {
        Queue<string> arguments = new(args);
        ArgUtils argUtils = new(action, ShowUsage, arguments);

        Args argsData = new()
        {
            Threshold = 0.4f,
            Retries = [30, 30, 300, 300, 3000, 3000]
        };

        if (string.IsNullOrEmpty(argsData.GitHubToken))
        {
            ShowUsage("Environment variable GITHUB_TOKEN is empty.", action);
            return null;
        }

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--repo":
                    if (!argUtils.TryDequeueRepoList("--repo", out string? org, out List<string>? repos))
                    {
                        return null;
                    }
                    argsData.Org = org;
                    argsData.Repos = repos;
                    break;

                case "--label-prefix":
                    if (!argUtils.TryDequeueLabelPrefix("--label-prefix", out Func<string, bool>? labelPredicate))
                    {
                        return null;
                    }
                    argsData.LabelPredicate = new(labelPredicate);
                    break;

                case "--excluded-authors":
                    if (!argUtils.TryDequeueStringArray("--excluded-authors", out string[]? excludedAuthors))
                    {
                        return null;
                    }
                    argsData.ExcludedAuthors = excludedAuthors;
                    break;

                case "--threshold":
                    if (!argUtils.TryDequeueFloat("--threshold", out float? threshold))
                    {
                        return null;
                    }
                    argsData.Threshold = threshold.Value;
                    break;

                case "--issues-model":
                    if (!argUtils.TryDequeuePath("--issues-model", out string? IssuesModelPath))
                    {
                        return null;
                    }
                    argsData.IssuesModelPath = IssuesModelPath;
                    break;

                case "--issues-limit":
                    if (!argUtils.TryDequeueInt("--issues-limit", out int? IssuesLimit))
                    {
                        return null;
                    }
                    argsData.IssuesLimit = IssuesLimit;
                    break;

                case "--pulls-model":
                    if (!argUtils.TryDequeuePath("--pulls-model", out string? PullsModelPath))
                    {
                        return null;
                    }
                    argsData.PullsModelPath = PullsModelPath;
                    break;

                case "--pulls-limit":
                    if (!argUtils.TryDequeueInt("--pulls-limit", out int? PullsLimit))
                    {
                        return null;
                    }
                    argsData.PullsLimit = PullsLimit;
                    break;

                case "--page-size":
                    if (!argUtils.TryDequeueInt("--page-size", out int? pageSize))
                    {
                        return null;
                    }
                    argsData.PageSize = pageSize;
                    break;

                case "--page-limit":
                    if (!argUtils.TryDequeueInt("--page-limit", out int? pageLimit))
                    {
                        return null;
                    }
                    argsData.PageLimit = pageLimit;
                    break;

                case "--retries":
                    if (!argUtils.TryDequeueIntArray("--retries", out int[]? retries))
                    {
                        return null;
                    }
                    argsData.Retries = retries;
                    break;

                case "--verbose":
                    argsData.Verbose = true;
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}", action);
                    return null;
            }
        }

        if (argsData.Org is null || argsData.Repos.Count == 0 || argsData.LabelPredicate is null ||
            (argsData.IssuesModelPath is null && argsData.PullsModelPath is null))
        {
            ShowUsage(null, action);
            return null;
        }

        return argsData;
    }
}
