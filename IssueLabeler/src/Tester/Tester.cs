// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;
using Microsoft.ML.Data;
using Actions.Core;
using Actions.Core.Extensions;
using Actions.Core.Markdown;
using Actions.Core.Services;
using Actions.Core.Summaries;
using GitHubClient;

using var provider = new ServiceCollection()
    .AddGitHubActionsCore()
    .BuildServiceProvider();

var action = provider.GetRequiredService<ICoreService>();
var config = Args.Parse(args, action);
if (config is not Args argsData) return 1;

List<Task<(Type ItemType, TestStats Stats)>> tasks = [];

if (argsData.IssuesModelPath is not null)
{
    tasks.Add(Task.Run(() => TestIssues()));
}

if (argsData.PullsModelPath is not null)
{
    tasks.Add(Task.Run(() => TestPullRequests()));
}

var (results, success) = await App.RunTasks(tasks, action);

foreach (var (itemType, stats) in results)
{
    AlertType resultAlert = (stats.MatchesPercentage >= 0.65f && stats.MismatchesPercentage < 0.15f) ? AlertType.Note : AlertType.Warning;

    action.Summary.AddPersistent(summary =>
    {
        summary.AddAlert($"**{stats.Total}** items were tested with **{stats.MatchesPercentage:P2} matches** and **{stats.MismatchesPercentage:P2} mismatches**.", resultAlert);
        summary.AddRawMarkdown($"Testing complete. **{stats.Total}** items tested, with the following results.", true);
        summary.AddNewLine();

        SummaryTableRow headerRow = new([
            new("", Header: true),
            new("Total", Header: true),
            new("Matches", Header: true),
            new("Mismatches", Header: true),
            new("No Prediction", Header: true),
            new("No Existing Label", Header: true)
        ]);

        SummaryTableRow countsRow = new([
            new("Count", Header: true),
            new($"{stats.Total}"),
            new($"{stats.Matches}"),
            new($"{stats.Mismatches}"),
            new($"{stats.NoPrediction}"),
            new($"{stats.NoExisting}")
        ]);

        SummaryTableRow percentageRow = new([
            new("Percentage", Header: true),
            new($""),
            new($"{stats.MatchesPercentage:P2}"),
            new($"{stats.MismatchesPercentage:P2}"),
            new($"{stats.NoPredictionPercentage:P2}"),
            new($"{stats.NoExistingPercentage:P2}")
        ]);

        summary.AddMarkdownTable(new(headerRow, [countsRow, percentageRow]));
        summary.AddNewLine();
        summary.AddMarkdownList([
            "**Matches**: The predicted label matches the existing label, including when no prediction is made and there is no existing label. Correct prediction.",
            "**Mismatches**: The predicted label _does not match_ the existing label. Incorrect prediction.",
            "**No Prediction**: No prediction was made, but the existing item had a label. Incorrect prediction.",
            "**No Existing Label**: A prediction was made, but there was no existing label. Incorrect prediction."
        ]);
        summary.AddNewLine();
        summary.AddAlert($"If the **Matches** percentage is **at least 65%** and the **Mismatches** percentage is **less than 15%**, the model testing is considered favorable.", AlertType.Tip);
    });
}

await action.Summary.WritePersistentAsync();
return success ? 0 : 1;

async Task<(Type, TestStats)> TestIssues()
{
    var predictor = GetPredictionEngine<Issue>(argsData.IssuesModelPath);
    var stats = new TestStats();

    async IAsyncEnumerable<Issue> DownloadIssues(string githubToken, string repo)
    {
        await foreach (var result in GitHubApi.DownloadIssues(githubToken, argsData.Org, repo, argsData.LabelPredicate, argsData.IssuesLimit, argsData.PageSize, argsData.PageLimit, argsData.Retries, argsData.ExcludedAuthors, action, argsData.Verbose))
        {
            yield return new(repo, result.Issue, argsData.LabelPredicate);
        }
    }

    action.WriteInfo($"Testing issues from {argsData.Repos.Count} repositories.");

    foreach (var repo in argsData.Repos)
    {
        await action.WriteStatusAsync($"Downloading and testing issues from {argsData.Org}/{repo}.");

        await foreach (var issue in DownloadIssues(argsData.GitHubToken, repo))
        {
            TestPrediction(issue, predictor, stats);
        }

        await action.WriteStatusAsync($"Finished testing issues from {argsData.Org}/{repo}.");
    }

    return (typeof(Issue), stats);
}

async Task<(Type, TestStats)> TestPullRequests()
{
    var predictor = GetPredictionEngine<PullRequest>(argsData.PullsModelPath);
    var stats = new TestStats();

    async IAsyncEnumerable<PullRequest> DownloadPullRequests(string githubToken, string repo)
    {
        await foreach (var result in GitHubApi.DownloadPullRequests(githubToken, argsData.Org, repo, argsData.LabelPredicate, argsData.PullsLimit, argsData.PageSize, argsData.PageLimit, argsData.Retries, argsData.ExcludedAuthors, action, argsData.Verbose))
        {
            yield return new(repo, result.PullRequest, argsData.LabelPredicate);
        }
    }

    foreach (var repo in argsData.Repos)
    {
        await action.WriteStatusAsync($"Downloading and testing pull requests from {argsData.Org}/{repo}.");

        await foreach (var pull in DownloadPullRequests(argsData.GitHubToken, repo))
        {
            TestPrediction(pull, predictor, stats);
        }

        await action.WriteStatusAsync($"Finished testing pull requests from {argsData.Org}/{repo}.");
    }

    return (typeof(PullRequest), stats);
}

static string GetStats(List<float> values)
{
    if (values.Count == 0)
    {
        return "N/A";
    }

    float min = values.Min();
    float average = values.Average();
    float max = values.Max();
    double deviation = Math.Sqrt(values.Average(v => Math.Pow(v - average, 2)));

    return $"{min} | {average} | {max} | {deviation}";
}

PredictionEngine<T, LabelPrediction> GetPredictionEngine<T>(string modelPath) where T : Issue
{
    var context = new MLContext();
    var model = context.Model.Load(modelPath, out _);

    return context.Model.CreatePredictionEngine<T, LabelPrediction>(model);
}

void TestPrediction<T>(T result, PredictionEngine<T, LabelPrediction> predictor, TestStats stats) where T : Issue
{
    var itemType = typeof(T) == typeof(PullRequest) ? "Pull Request" : "Issue";

    (string? predictedLabel, float? score) = GetPrediction(
        predictor,
        result,
        argsData.Threshold);

    if (predictedLabel is null && result.Label is not null)
    {
        stats.NoPrediction++;
    }
    else if (predictedLabel is not null && result.Label is null)
    {
        stats.NoExisting++;
    }
    else if (predictedLabel?.ToLower() == result.Label?.ToLower())
    {
        stats.Matches++;

        if (score.HasValue)
        {
            stats.MatchScores.Add(score.Value);
        }
    }
    else
    {
        stats.Mismatches++;

        if (score.HasValue)
        {
            stats.MismatchScores.Add(score.Value);
        }
    }

    action.StartGroup($"{itemType} {argsData.Org}/{result.Repo}#{result.Number} - Predicted: {(predictedLabel ?? "<NONE>")} - Existing: {(result.Label ?? "<NONE>")}");
    action.WriteInfo($"Total        : {stats.Total}");
    action.WriteInfo($"Matches      : {stats.Matches} ({stats.MatchesPercentage:P2}) - Min | Avg | Max | StdDev: {GetStats(stats.MatchScores)}");
    action.WriteInfo($"Mismatches   : {stats.Mismatches} ({stats.MismatchesPercentage:P2}) - Min | Avg | Max | StdDev: {GetStats(stats.MismatchScores)}");
    action.WriteInfo($"No Prediction: {stats.NoPrediction} ({stats.NoPredictionPercentage:P2})");
    action.WriteInfo($"No Existing  : {stats.NoExisting} ({stats.NoExistingPercentage:P2})");
    action.EndGroup();
}

(string? PredictedLabel, float? PredictionScore) GetPrediction<T>(PredictionEngine<T, LabelPrediction> predictor, T issueOrPull, float? threshold) where T : Issue
{
    var prediction = predictor.Predict(issueOrPull);
    var itemType = typeof(T) == typeof(PullRequest) ? "Pull Request" : "Issue";

    if (prediction.Score is null || prediction.Score.Length == 0)
    {
        action.WriteInfo($"No prediction was made for {itemType} {argsData.Org}/{issueOrPull.Repo}#{issueOrPull.Number}.");
        return (null, null);
    }

    VBuffer<ReadOnlyMemory<char>> labels = default;
    predictor.OutputSchema[nameof(LabelPrediction.Score)].GetSlotNames(ref labels);

    var bestScore = prediction.Score
        .Select((score, index) => new
        {
            Score = score,
            Label = labels.GetItemOrDefault(index).ToString()
        })
        .OrderByDescending(p => p.Score)
        .FirstOrDefault(p => threshold is null || p.Score >= threshold);

    return bestScore is not null ? (bestScore.Label, bestScore.Score) : ((string?)null, (float?)null);
}

class TestStats
{
    public TestStats() { }

    public int Matches { get; set; } = 0;
    public int Mismatches { get; set; } = 0;
    public int NoPrediction { get; set; } = 0;
    public int NoExisting { get; set; } = 0;

    public float Total => Matches + Mismatches + NoPrediction + NoExisting;

    public float MatchesPercentage => (float)Matches / Total;
    public float MismatchesPercentage => (float)Mismatches / Total;
    public float NoPredictionPercentage => (float)NoPrediction / Total;
    public float NoExistingPercentage => (float)NoExisting / Total;

    public List<float> MatchScores => [];
    public List<float> MismatchScores => [];
}
