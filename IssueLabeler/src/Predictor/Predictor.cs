// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;
using Microsoft.ML.Data;
using Actions.Core;
using Actions.Core.Extensions;
using Actions.Core.Markdown;
using Actions.Core.Services;
using GitHubClient;

using var provider = new ServiceCollection()
    .AddGitHubActionsCore()
    .BuildServiceProvider();

var action = provider.GetRequiredService<ICoreService>();
if (Args.Parse(args, action) is not Args argsData) return 1;

List<Task<(ModelType Type, ulong Number, bool Success, string[] Output)>> tasks = new();

if (argsData.IssuesModelPath is not null && argsData.Issues is not null)
{
    action.WriteInfo($"Loading prediction engine for issues model: {argsData.IssuesModelPath}");
    var issueContext = new MLContext();
    var issueModel = issueContext.Model.Load(argsData.IssuesModelPath, out _);
    var issuePredictor = issueContext.Model.CreatePredictionEngine<Issue, LabelPrediction>(issueModel);
    action.WriteInfo($"Issues prediction engine ready.");

    foreach (ulong issueNumber in argsData.Issues)
    {
        var result = await GitHubApi.GetIssue(argsData.GitHubToken, argsData.Org, argsData.Repo, issueNumber, argsData.Retries, action, argsData.Verbose);

        if (result is null)
        {
            action.WriteNotice($"[Issue {argsData.Org}/{argsData.Repo}#{issueNumber}] could not be found or downloaded. Skipped.");
            continue;
        }

        if (argsData.ExcludedAuthors is not null && result.Author?.Login is not null && argsData.ExcludedAuthors.Contains(result.Author.Login, StringComparer.InvariantCultureIgnoreCase))
        {
            action.WriteNotice($"[Issue {argsData.Org}/{argsData.Repo}#{issueNumber}] Author '{result.Author.Login}' is in excluded list. Skipped.");
            continue;
        }

        tasks.Add(Task.Run(() => ProcessPrediction(
            issuePredictor,
            issueNumber,
            new Issue(result),
            argsData.LabelPredicate,
            argsData.DefaultLabel,
            ModelType.Issue,
            argsData.Retries,
            argsData.Test
        )));

        action.WriteInfo($"[Issue {argsData.Org}/{argsData.Repo}#{issueNumber}] Queued for prediction.");
    }
}

if (argsData.PullsModelPath is not null && argsData.Pulls is not null)
{
    action.WriteInfo($"Loading prediction engine for pulls model: {argsData.IssuesModelPath}");
    var pullContext = new MLContext();
    var pullModel = pullContext.Model.Load(argsData.PullsModelPath, out _);
    var pullPredictor = pullContext.Model.CreatePredictionEngine<PullRequest, LabelPrediction>(pullModel);
    action.WriteInfo($"Pulls prediction engine ready.");

    foreach (ulong pullNumber in argsData.Pulls)
    {
        var result = await GitHubApi.GetPullRequest(argsData.GitHubToken, argsData.Org, argsData.Repo, pullNumber, argsData.Retries, action, argsData.Verbose);

        if (result is null)
        {
            action.WriteNotice($"[Pull Request {argsData.Org}/{argsData.Repo}#{pullNumber}] could not be found or downloaded. Skipped.");
            continue;
        }

        if (argsData.ExcludedAuthors is not null && result.Author?.Login is not null && argsData.ExcludedAuthors.Contains(result.Author.Login))
        {
            action.WriteNotice($"[Pull Request {argsData.Org}/{argsData.Repo}#{pullNumber}] Author '{result.Author.Login}' is in excluded list. Skipped.");
            continue;
        }

        tasks.Add(Task.Run(() => ProcessPrediction(
            pullPredictor,
            pullNumber,
            new PullRequest(result),
            argsData.LabelPredicate,
            argsData.DefaultLabel,
            ModelType.PullRequest,
            argsData.Retries,
            argsData.Test
        )));

        action.WriteInfo($"[Pull Request {argsData.Org}/{argsData.Repo}#{pullNumber}] Queued for prediction.");
    }
}

var (predictionResults, success) = await App.RunTasks(tasks, action);

foreach (var prediction in predictionResults)
{
    string predictionResult = $"""
        [{prediction.Type} {argsData.Org}/{argsData.Repo}#{prediction.Number}{(prediction.Success ? "" : " FAILURE")}]
          {string.Join("\n  ", prediction.Output)}
        """;

    action.WriteNotice(predictionResult);

    if (!prediction.Success)
    {
        action.Summary.AddAlert(predictionResult, AlertType.Warning);
    }
}

await action.Summary.WriteAsync();
return success ? 0 : 1;

async Task<(ModelType, ulong, bool, string[])> ProcessPrediction<T>(PredictionEngine<T, LabelPrediction> predictor, ulong number, T issueOrPull, Func<string, bool> labelPredicate, string? defaultLabel, ModelType type, int[] retries, bool test) where T : Issue
{
    List<string> output = new();
    string? error = null;

    string typeName = type == ModelType.PullRequest ? "Pull Request" : "Issue";

    if (issueOrPull.HasMoreLabels)
    {
        output.Add($"[{typeName} {argsData.Org}/{argsData.Repo}#{number}] No action taken. Too many labels applied already; cannot be sure no applicable label is already applied.");
        return (type, number, true, output.ToArray());
    }

    var applicableLabel = issueOrPull.Labels?.FirstOrDefault(labelPredicate);

    bool hasDefaultLabel =
        (defaultLabel is not null) &&
        (issueOrPull.Labels?.Any(l => l.Equals(defaultLabel, StringComparison.OrdinalIgnoreCase)) ?? false);

    if (applicableLabel is not null)
    {
        output.Add($"Applicable label '{applicableLabel}' already exists.");

        if (hasDefaultLabel && defaultLabel is not null)
        {
            if (!test)
            {
                error = await GitHubApi.RemoveLabel(argsData.GitHubToken, argsData.Org, argsData.Repo, typeName, number, defaultLabel, argsData.Retries, action);
            }

            output.Add(error ?? $"Removed default label '{defaultLabel}'.");
        }

        return (type, number, error is null, output.ToArray());
    }

    var prediction = predictor.Predict(issueOrPull);

    if (prediction.Score is null || prediction.Score.Length == 0)
    {
        output.Add("No prediction was made.");
        return (type, number, true, output.ToArray());
    }

    VBuffer<ReadOnlyMemory<char>> labels = default;
    predictor.OutputSchema[nameof(LabelPrediction.Score)].GetSlotNames(ref labels);

    var predictions = prediction.Score
        .Select((score, index) => new
        {
            Score = score,
            Label = labels.GetItemOrDefault(index).ToString()
        })
        // Ensure predicted labels match the expected predicate
        .Where(prediction => labelPredicate(prediction.Label))
        // Capture the top 3 for including in the output
        .OrderByDescending(p => p.Score)
        .Take(3);

    output.Add("Label predictions:");
    output.AddRange(predictions.Select(p => $"  '{p.Label}' - Score: {p.Score}"));

    var bestScore = predictions.FirstOrDefault(p => p.Score >= argsData.Threshold);
    output.Add(bestScore is not null ?
        $"Label '{bestScore.Label}' meets threshold of {argsData.Threshold}." :
        $"No label meets the threshold of {argsData.Threshold}.");

    if (bestScore is not null)
    {
        if (!test)
        {
            error = await GitHubApi.AddLabel(argsData.GitHubToken, argsData.Org, argsData.Repo, typeName, number, bestScore.Label, retries, action);
        }

        output.Add(error ?? $"Added label '{bestScore.Label}'");

        if (error is not null)
        {
            return (type, number, false, output.ToArray());
        }

        if (hasDefaultLabel && defaultLabel is not null)
        {
            if (!test)
            {
                error = await GitHubApi.RemoveLabel(argsData.GitHubToken, argsData.Org, argsData.Repo, typeName, number, defaultLabel, retries, action);
            }

            output.Add(error ?? $"Removed default label '{defaultLabel}'");
        }

        return (type, number, error is null, output.ToArray());
    }

    if (defaultLabel is not null)
    {
        if (hasDefaultLabel)
        {
            output.Add($"Default label '{defaultLabel}' is already applied.");
        }
        else
        {
            if (!test)
            {
                error = await GitHubApi.AddLabel(argsData.GitHubToken, argsData.Org, argsData.Repo, typeName, number, defaultLabel, argsData.Retries, action);
            }

            output.Add(error ?? $"Applied default label '{defaultLabel}'.");
        }
    }

    return (type, number, error is null, output.ToArray());
}
