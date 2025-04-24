// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Actions.Core.Services;

public struct Args
{
    public string GitHubToken => Environment.GetEnvironmentVariable("GITHUB_TOKEN")!;
    public string Org { get; set; }
    public string Repo { get; set; }
    public float Threshold { get; set; }
    public Func<string, bool> LabelPredicate { get; set; }
    public string[]? ExcludedAuthors { get; set; }
    public string? IssuesModelPath { get; set; }
    public List<ulong>? Issues { get; set; }
    public string? PullsModelPath { get; set; }
    public List<ulong>? Pulls { get; set; }
    public string? DefaultLabel { get; set; }
    public int[] Retries { get; set; }
    public bool Verbose { get; set; }
    public bool Test { get; set; }

    static void ShowUsage(string? message, ICoreService action)
    {
        action.WriteNotice($$"""
            ERROR: Invalid or missing inputs.{{(message is null ? "" : " " + message)}}

            Required environment variables:
              GITHUB_TOKEN            GitHub token to be used for API calls.

            Inputs are specified as ALL_CAPS environment variables prefixed with 'INPUT_'.

            Required inputs:
              REPO                    GitHub repository in the format {org}/{repo}.
                                      Defaults to: GITHUB_REPOSITORY environment variable.
              LABEL_PREFIX            Prefix for label predictions.
                                      Must end with a non-alphanumeric character.

            Required inputs for predicting issue labels:
              ISSUES_MODEL            Path to the issue prediction model file (ZIP file).
              ISSUES                  Comma-separated list of issue number ranges.
                                      Example: 1-3,7,5-9.

            Required inputs for predicting pull request labels:
              PULLS_MODEL             Path to the pull request prediction model file (ZIP file).
              PULLS                   Comma-separated list of pull request number ranges.
                                      Example: 1-3,7,5-9.

            Optional inputs:
              THRESHOLD               Minimum prediction confidence threshold. Range (0,1].
                                      Defaults to: 0.4.
              DEFAULT_LABEL           Label to apply if no label is predicted.
              EXCLUDED_AUTHORS        Comma-separated list of authors to exclude.
              RETRIES                 Comma-separated retry delays in seconds.
                                      Defaults to: 30,30,300,300,3000,3000.
              TEST                    Run in test mode, outputting predictions without applying labels.
                                      Must be one of: true, false, TRUE, FALSE
              VERBOSE                 Enable verbose output.
                                      Must be one of: true, false, TRUE, FALSE
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args, ICoreService action)
    {
        ArgUtils argUtils = new(action, ShowUsage);
        argUtils.TryGetRepo("repo", out var org, out var repo);
        argUtils.TryGetLabelPrefix("label_prefix", out var labelPredicate);
        argUtils.TryGetPath("issues_model", out var issuesModelPath);
        argUtils.TryGetNumberRanges("issues", out var issues);
        argUtils.TryGetPath("pulls_model", out var pullsModelPath);
        argUtils.TryGetNumberRanges("pulls", out var pulls);
        argUtils.TryGetStringArray("excluded_authors", out var excludedAuthors);
        argUtils.TryGetFloat("threshold", out var threshold);
        argUtils.TryGetIntArray("retries", out var retries);
        argUtils.TryGetString("default_label", out var defaultLabel);
        argUtils.TryGetFlag("test", out var test);
        argUtils.TryGetFlag("verbose", out var verbose);

        if (org is null || repo is null || threshold is null || labelPredicate is null ||
            (issues is null && pulls is null))
        {
            ShowUsage(null, action);
            return null;
        }

        Args argsData = new()
        {
            Org = org,
            Repo = repo,
            LabelPredicate = labelPredicate,
            DefaultLabel = defaultLabel,
            IssuesModelPath = issuesModelPath,
            Issues = issues,
            PullsModelPath = pullsModelPath,
            Pulls = pulls,
            ExcludedAuthors = excludedAuthors,
            Threshold = threshold ?? 0.4f,
            Retries = retries ?? [30, 30, 300, 300, 3000, 3000],
            Test = test ?? false,
            Verbose = verbose ?? false
        };

        if (string.IsNullOrEmpty(argsData.GitHubToken))
        {
            ShowUsage("Environment variable GITHUB_TOKEN is empty.", action);
            return null;
        }

        return argsData;
    }
}
