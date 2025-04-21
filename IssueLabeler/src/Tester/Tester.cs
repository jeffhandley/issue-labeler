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
var config = Args.Parse(args);
if (config is not Args argsData) return;

List<Task> tasks = [];

if (argsData.IssuesModelPath is not null)
{
    tasks.Add(Task.Run(() => TestIssues()));
}

if (argsData.PullsModelPath is not null)
{
    tasks.Add(Task.Run(() => TestPullRequests()));
}

await Task.WhenAll(tasks);

async IAsyncEnumerable<T> ReadData<T>(string dataPath, Func<ulong, string[], T> readLine, int? rowLimit)
{
    var allLines = File.ReadLinesAsync(dataPath);
    ulong rowNum = 0;
    rowLimit ??= 50000;

    await foreach (var line in allLines)
    {
        // Skip the header row
        if (rowNum == 0)
        {
            rowNum++;
            continue;
        }

        string[] columns = line.Split('\t');
        yield return readLine(rowNum, columns);

        if ((int)rowNum++ >= rowLimit)
        {
            break;
        }
    }
}

async IAsyncEnumerable<Issue> DownloadIssues(string githubToken, string org, string repo)
{
    await foreach (var result in GitHubApi.DownloadIssues(githubToken, org, repo, argsData.LabelPredicate, argsData.IssuesLimit, 100, 1000, [30, 30, 30], argsData.ExcludedAuthors ?? []))
    {
        yield return new(result.Issue, argsData.LabelPredicate);
    }
}

async Task TestIssues()
{
    if (argsData.IssuesDataPath is not null)
    {
        var issueList = ReadData(argsData.IssuesDataPath, (num, columns) => new Issue()
        {
            Number = num,
            Label = columns[0],
            Title = columns[1],
            Body = columns[2]
        }, argsData.IssuesLimit);

        await TestPredictions(issueList, argsData.IssuesModelPath);
        return;
    }

    if (argsData.GithubToken is not null && argsData.Org is not null && argsData.Repos is not null)
    {
        foreach (var repo in argsData.Repos)
        {
            action.WriteInfo($"Downloading and testing issues from {argsData.Org}/{repo}.");

            var issueList = DownloadIssues(argsData.GithubToken, argsData.Org, repo);
            await TestPredictions(issueList, argsData.IssuesModelPath);
        }
    }
}

async IAsyncEnumerable<PullRequest> DownloadPullRequests(string githubToken, string org, string repo)
{
    await foreach (var result in GitHubApi.DownloadPullRequests(githubToken, org, repo, argsData.LabelPredicate, argsData.PullsLimit, 25, 4000, [30, 30, 30], argsData.ExcludedAuthors ?? []))
    {
        yield return new(result.PullRequest, argsData.LabelPredicate);
    }
}

async Task TestPullRequests()
{
    if (argsData.PullsDataPath is not null)
    {
        var pullList = ReadData(argsData.PullsDataPath, (num, columns) => new PullRequest()
        {
            Number = num,
            Label = columns[0],
            Title = columns[1],
            Body = columns[2],
            FileNames = columns[3],
            FolderNames = columns[4]
        }, argsData.PullsLimit);

        await TestPredictions(pullList, argsData.PullsModelPath);
        return;
    }

    if (argsData.GithubToken is not null && argsData.Org is not null && argsData.Repos is not null)
    {
        foreach (var repo in argsData.Repos)
        {
            action.WriteInfo($"Downloading and testing pull requests from {argsData.Org}/{repo}.");

            var pullList = DownloadPullRequests(argsData.GithubToken, argsData.Org, repo);
            await TestPredictions(pullList, argsData.PullsModelPath);
        }
    }
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

async Task TestPredictions<T>(IAsyncEnumerable<T> results, string modelPath) where T : Issue
{
    var context = new MLContext();
    var model = context.Model.Load(modelPath, out _);
    var predictor = context.Model.CreatePredictionEngine<T, LabelPrediction>(model);
    var itemType = typeof(T) == typeof(PullRequest) ? "Pull Request" : "Issue";

    int matches = 0;
    int mismatches = 0;
    int noPrediction = 0;
    int noExisting = 0;
    float total = 0;

    List<float> matchScores = [];
    List<float> mismatchScores = [];

    await foreach (var result in results)
    {
        (string? predictedLabel, float? score) = GetPrediction(
            predictor,
            result,
            argsData.Threshold);

        if (predictedLabel is null && result.Label is not null)
        {
            noPrediction++;
        }
        else if (predictedLabel is not null && result.Label is null)
        {
            noExisting++;
        }
        else if (predictedLabel?.ToLower() == result.Label?.ToLower())
        {
            matches++;

            if (score.HasValue)
            {
                matchScores.Add(score.Value);
            }
        }
        else
        {
            mismatches++;

            if (score.HasValue)
            {
                mismatchScores.Add(score.Value);
            }
        }

        total = matches + mismatches + noPrediction + noExisting;
        action.StartGroup($"{itemType} #{result.Number} - Predicted: {(predictedLabel ?? "<NONE>")} - Existing: {(result.Label ?? "<NONE>")}");
        action.WriteInfo($"Matches      : {matches} ({(float)matches / total:P2}) - Min | Avg | Max | StdDev: {GetStats(matchScores)}");
        action.WriteInfo($"Mismatches   : {mismatches} ({(float)mismatches / total:P2}) - Min | Avg | Max | StdDev: {GetStats(mismatchScores)}");
        action.WriteInfo($"No Prediction: {noPrediction} ({(float)noPrediction / total:P2})");
        action.WriteInfo($"No Existing  : {noExisting} ({(float)noExisting / total:P2})");
        action.EndGroup();
    }

    action.WriteInfo("Test Complete");

    SummaryTableRow headerRow = new([
        new("", Header: true),
        new("Matches", Header: true),
        new("Mismatches", Header: true),
        new("No Prediction", Header: true),
        new("No Existing Label", Header: true)
    ]);

    SummaryTableRow countsRow = new([
        new("Count", Header: true, Alignment: TableColumnAlignment.Left),
        new($"{matches}", Alignment: TableColumnAlignment.Right),
        new($"{mismatches}", Alignment: TableColumnAlignment.Right),
        new($"{noPrediction}", Alignment: TableColumnAlignment.Right),
        new($"{noExisting}", Alignment: TableColumnAlignment.Right)
    ]);

    float matchPercentage = (float)matches / total;
    float mismatchPercentage = (float)mismatches / total;

    AlertType resultAlert = (matchPercentage >= 0.65f && mismatchPercentage < 0.15f) ? AlertType.Note : AlertType.Warning;
    action.Summary.AddAlert($"**{total}** items were tested with **{matchPercentage:P2} matches** and **{mismatchPercentage:P2} mismatches**. These results are considered favorable.", resultAlert);

    SummaryTableRow percentageRow = new([
        new("Percentage", Header: true, Alignment: TableColumnAlignment.Left),
        new($"{matchPercentage:P2}", Alignment: TableColumnAlignment.Right),
        new($"{mismatchPercentage:P2}", Alignment: TableColumnAlignment.Right),
        new($"{(float)noPrediction / total:P2}", Alignment: TableColumnAlignment.Right),
        new($"{(float)noExisting / total:P2}", Alignment: TableColumnAlignment.Right)
    ]);

    action.Summary.AddRaw($"Testing complete. **{total}** items tested, with the following results.");
    action.Summary.AddTable([headerRow, countsRow, percentageRow]);
    action.Summary.AddNewLine();
    action.Summary.AddList([
        "**Matches**: The predicted label matches the existing label, including when no prediction is made and there is no existing label. Correct prediction.",
        "**Mismatches**: The predicted label _does not match_ the existing label. Incorrect prediction.",
        "**No Prediction**: No prediction was made, but the existing item had a label. Incorrect prediction.",
        "**No Existing Label**: A prediction was made, but there was no existing label. Incorrect prediction."
    ]);
    action.Summary.AddAlert($"If the **Matches** percentage is **at least 65%** and the **Mismatches** percentage is **less than 15%**, the model testing is considered favorable.", AlertType.Tip);
}

(string? PredictedLabel, float? PredictionScore) GetPrediction<T>(PredictionEngine<T, LabelPrediction> predictor, T issueOrPull, float? threshold) where T : Issue
{
    var prediction = predictor.Predict(issueOrPull);
    var itemType = typeof(T) == typeof(PullRequest) ? "Pull Request" : "Issue";

    if (prediction.Score is null || prediction.Score.Length == 0)
    {
        action.WriteInfo($"No prediction was made for {itemType} #{issueOrPull.Number}.");
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
