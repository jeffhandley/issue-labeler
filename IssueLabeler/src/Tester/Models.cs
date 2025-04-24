// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public class Issue
{
    public string Repo { get; set; }
    public ulong Number { get; set; }
    public string? Label { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }

    // The Area and Description properties allow loading
    // models from the issue-labeler implementation
    public string? Area { get => Label; }
    public string? Description { get => Body; }

    public Issue(string repo, GitHubClient.Issue issue, Predicate<string> labelPredicate)
    {
        Repo = repo;
        Number = issue.Number;
        Title = issue.Title;
        Body = issue.Body;
        Label = issue.Labels.HasNextPage ?
            (string?) null :
            issue.LabelNames?.SingleOrDefault(l => labelPredicate(l));
    }
}

public class PullRequest : Issue
{
    public string? FileNames { get; set; }
    public string? FolderNames { get; set; }

    public PullRequest(string repo, GitHubClient.PullRequest pull, Predicate<string> labelPredicate) : base(repo, pull, labelPredicate)
    {
        FileNames = string.Join(' ', pull.FileNames);
        FolderNames = string.Join(' ', pull.FolderNames);
    }
}

public class LabelPrediction
{
    public string? PredictedLabel { get; set; }
    public float[]? Score { get; set; }
}
