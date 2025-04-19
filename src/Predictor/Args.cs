// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Actions.Core;
using Actions.Core.Extensions;
using Actions.Core.Services;

public struct Args
{
    public string Org { get; set; }
    public string Repo { get; set; }
    public string GithubToken { get; set; }
    public string? IssueModelPath { get; set; }
    public List<ulong>? IssueNumbers { get; set; }
    public string? PullModelPath { get; set; }
    public List<ulong>? PullNumbers { get; set; }
    public float Threshold { get; set; }
    public Func<string, bool> LabelPredicate { get; set; }
    public string? DefaultLabel { get; set; }
    public int[] Retries { get; set; }
    public bool Verbose { get; set; }
    public string[]? ExcludedAuthors { get; set; }
    public bool Test { get; set; }

    static void ShowUsage(string? message = null)
    {
        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($$"""
            ERROR: Invalid or missing inputs.{{(message is null ? "" : " " + message)}}

            Required environment variables:
              GITHUB_TOKEN      GitHub token to be used for API calls.

            Required inputs:
              repo              GitHub repository in the format {org}/{repo}.
              label_prefix      Prefix for label predictions. Must end with a non-alphanumeric character.

            Required for predicting issue labels:
              issue_model       Path to existing issue prediction model file (ZIP file).
              issue_numbers     Comma-separated list of issue number ranges. Example: 1-3,7,5-9.

            Required for predicting pull request labels:
              pull_model        Path to existing pull request prediction model file (ZIP file).
              pull_numbers      Comma-separated list of pull request number ranges. Example: 1-3,7,5-9.

            Optional inputs:
              default_label     Default label to use if no label is predicted.
              threshold         Minimum prediction confidence threshold. Range (0,1]. Default 0.4.
              retries           Comma-separated retry delays in seconds. Default: 30,30,300,300,3000,3000.
              excluded_authors  Comma-separated list of authors to exclude.
              token             GitHub token. Default: read from GITHUB_TOKEN env var.
              test              Run in test mode, outputting predictions without applying labels.
              verbose           Enable verbose output.
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args)
    {
        using var provider = new ServiceCollection()
            .AddGitHubActionsCore()
            .BuildServiceProvider();

        var action = provider.GetRequiredService<ICoreService>();
        ArgUtils.TryGetRequiredString("GITHUB_TOKEN", Environment.GetEnvironmentVariable, out var token, ShowUsage);
        ArgUtils.TryParseRepo("repo", i => action.GetInput(i), out var org, out var repo, ShowUsage);
        ArgUtils.TryParseLabelPrefix("label_prefix", i => action.GetInput(i), out var labelPredicate, ShowUsage);
        ArgUtils.TryParsePath("issue_model", i => action.GetInput(i), out var issueModelPath);
        ArgUtils.TryParseNumberRanges("issue_numbers", i => action.GetInput(i), out var issueNumbers, ShowUsage);
        ArgUtils.TryParsePath("pull_model", i => action.GetInput(i), out var pullModelPath);
        ArgUtils.TryParseNumberRanges("pull_numbers", i => action.GetInput(i), out var pullNumbers, ShowUsage);
        ArgUtils.TryParseStringArray("excluded_authors", i => action.GetInput(i), out var excludedAuthors);
        ArgUtils.TryParseFloat("threshold", i => action.GetInput(i), out var threshold, ShowUsage);
        ArgUtils.TryParseIntArray("retries", i => action.GetInput(i), out var retries, ShowUsage);

        var defaultLabel = action.GetInput("default_label");
        ArgUtils.GetFlag("test", i => action.GetInput(i), out var test, ShowUsage);
        ArgUtils.GetFlag("verbose", i => action.GetInput(i), out var verbose, ShowUsage);

        if (token is null || org is null || repo is null || threshold is null || labelPredicate is null ||
            (issueModelPath is null != issueNumbers is null) ||
            (pullModelPath is null != pullNumbers is null) ||
            (issueModelPath is null && pullModelPath is null))
        {
            ShowUsage();
            return null;
        }

        return new()
        {
            GithubToken = token,
            Org = org,
            Repo = repo,
            LabelPredicate = labelPredicate,
            DefaultLabel = defaultLabel,
            IssueModelPath = issueModelPath,
            IssueNumbers = issueNumbers,
            PullModelPath = pullModelPath,
            PullNumbers = pullNumbers,
            ExcludedAuthors = excludedAuthors,
            Threshold = threshold ?? 0.4f,
            Retries = retries ?? [30, 30, 300, 300, 3000, 3000],
            Test = test ?? false,
            Verbose = verbose ?? false
        };
    }
}
