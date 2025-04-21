// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Actions.Core.Services;

public struct Args
{
    public string Org { get; set; }
    public string Repo { get; set; }
    public string GithubToken { get; set; }
    public string? IssuesModelPath { get; set; }
    public List<ulong>? Issues { get; set; }
    public string? PullsModelPath { get; set; }
    public List<ulong>? Pulls { get; set; }
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
              label_prefix      Prefix for label predictions.
                                Must end with a non-alphanumeric character.

            Required for predicting issue labels:
              issues_model      Path to the issue prediction model file (ZIP file).
              issues            Comma-separated list of issue number ranges.
                                Example: 1-3,7,5-9.

            Required for predicting pull request labels:
              pulls_model       Path to the pull request prediction model file (ZIP file).
              pulls             Comma-separated list of pull request number ranges.
                                Example: 1-3,7,5-9.

            Optional inputs:
              repo              GitHub repository in the format {org}/{repo}.
                                Defaults to: GITHUB_REPOSITORY environment variable.

              default_label     Label to apply if no label is predicted.

              threshold         Minimum prediction confidence threshold. Range (0,1].
                                Defaults to: 0.4.

              retries           Comma-separated retry delays in seconds.
                                Defaults to: 30,30,300,300,3000,3000.

              excluded_authors  Comma-separated list of authors to exclude.

              test              Run in test mode, outputting predictions without applying labels.
                                Must be one of: true, false, TRUE, FALSE

              verbose           Enable verbose output.
                                Must be one of: true, false, TRUE, FALSE
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args, ICoreService action)
    {
        ArgUtils.TryGetRequiredString("GITHUB_TOKEN", Environment.GetEnvironmentVariable, out var token, ShowUsage);
        ArgUtils.TryParseRepo("repo", i => action.GetInput(i), out var org, out var repo, ShowUsage);
        ArgUtils.TryParseLabelPrefix("label_prefix", i => action.GetInput(i), out var labelPredicate, ShowUsage);
        ArgUtils.TryParsePath("issues_model", i => action.GetInput(i), out var issuesModelPath);
        ArgUtils.TryParseNumberRanges("issues", i => action.GetInput(i), out var issues, ShowUsage);
        ArgUtils.TryParsePath("pulls_model", i => action.GetInput(i), out var pullsModelPath);
        ArgUtils.TryParseNumberRanges("pulls", i => action.GetInput(i), out var pulls, ShowUsage);
        ArgUtils.TryParseStringArray("excluded_authors", i => action.GetInput(i), out var excludedAuthors);
        ArgUtils.TryParseFloat("threshold", i => action.GetInput(i), out var threshold, ShowUsage);
        ArgUtils.TryParseIntArray("retries", i => action.GetInput(i), out var retries, ShowUsage);

        var defaultLabel = action.GetInput("default_label");
        ArgUtils.GetFlag("test", i => action.GetInput(i), out var test, ShowUsage);
        ArgUtils.GetFlag("verbose", i => action.GetInput(i), out var verbose, ShowUsage);

        if (token is null || org is null || repo is null || threshold is null || labelPredicate is null ||
            (issues is null && pulls is null))
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
    }
}
