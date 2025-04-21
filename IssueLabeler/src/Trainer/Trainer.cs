// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using static DataFileUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Text;
using Actions.Core;
using Actions.Core.Extensions;
using Actions.Core.Services;
using Actions.Core.Markdown;

using var provider = new ServiceCollection()
    .AddGitHubActionsCore()
    .BuildServiceProvider();

var action = provider.GetRequiredService<ICoreService>();

var config = Args.Parse(args, action);
if (config is not Args argsData)
{
    return;
}

if (argsData.IssuesDataPath is not null && argsData.IssuesModelPath is not null)
{
    await CreateModel(argsData.IssuesDataPath, argsData.IssuesModelPath, ModelType.Issue, action);
}

if (argsData.PullsDataPath is not null && argsData.PullsModelPath is not null)
{
    await CreateModel(argsData.PullsDataPath, argsData.PullsModelPath, ModelType.PullRequest, action);
}

await action.Summary.WriteAsync();

static async Task CreateModel(string dataPath, string modelPath, ModelType type, ICoreService action)
{
    if (!File.Exists(dataPath))
    {
        action.WriteNotice($"The data file '{dataPath}' does not exist.");
        action.Summary.AddAlert("The data file does not exist. Training cannot proceed.", AlertType.Caution);
        await action.Summary.WriteAsync();

        throw new InvalidOperationException($"The data file '{dataPath}' does not exist.");
    }

    int recordsCounted = File.ReadLines(dataPath).Take(10).Count();
    if (recordsCounted < 10)
    {
        action.WriteNotice($"The data file '{dataPath}' does not contain enough data for training. A minimum of 10 records is required, but only {recordsCounted} exist.");
        action.Summary.AddAlert($"Only {recordsCounted} items were found to be used for training. A minimum of 10 records is required. Cannot proceed with training.", AlertType.Caution);
        await action.Summary.WriteAsync();

        throw new InvalidOperationException($"The data file '{dataPath}' does not contain enough data for training. A minimum of 10 records is required, but only {recordsCounted} exist.");
    }

    action.WriteInfo("Loading data into train/test sets...");
    MLContext mlContext = new();

    TextLoader.Column[] columns = type == ModelType.Issue ? [
        new("Label", DataKind.String, 0),
        new("Title", DataKind.String, 1),
        new("Body", DataKind.String, 2),
    ] : [
        new("Label", DataKind.String, 0),
        new("Title", DataKind.String, 1),
        new("Body", DataKind.String, 2),
        new("FileNames", DataKind.String, 3),
        new("FolderNames", DataKind.String, 4)
    ];

    TextLoader.Options textLoaderOptions = new()
    {
        AllowQuoting = false,
        AllowSparse = false,
        EscapeChar = '"',
        HasHeader = true,
        ReadMultilines = false,
        Separators = ['\t'],
        TrimWhitespace = true,
        UseThreads = true,
        Columns = columns
    };

    var loader = mlContext.Data.CreateTextLoader(textLoaderOptions);
    var data = loader.Load(dataPath);
    var split = mlContext.Data.TrainTestSplit(data, testFraction: 0.2);

    action.WriteInfo("Building pipeline...");

    var xf = mlContext.Transforms;
    var pipeline = xf.Conversion.MapValueToKey(inputColumnName: "Label", outputColumnName: "LabelKey")
        .Append(xf.Text.FeaturizeText(
            "Features",
            new TextFeaturizingEstimator.Options(),
            columns.Select(c => c.Name).ToArray()))
        .AppendCacheCheckpoint(mlContext)
        .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey"))
        .Append(xf.Conversion.MapKeyToValue("PredictedLabel"));

    action.WriteInfo("Fitting the model with the training data set...");
    var trainedModel = pipeline.Fit(split.TrainSet);
    var testModel = trainedModel.Transform(split.TestSet);

    action.WriteInfo("Evaluating against the test set...");
    var metrics = mlContext.MulticlassClassification.Evaluate(testModel, labelColumnName: "LabelKey");

    action.Summary.AddRawMarkdown($"""
        * MacroAccuracy: {metrics.MacroAccuracy:0.####} (a value between 0 and 1; the closer to 1, the better)
        * MicroAccuracy: {metrics.MicroAccuracy:0.####} (a value between 0 and 1; the closer to 1, the better)
        * LogLoss: {metrics.LogLoss:0.####} (the closer to 0, the better)
        {(metrics.PerClassLogLoss.Count() > 0 ? $"    * Class 1: {metrics.PerClassLogLoss[0]:0.####}" : "")}
        {(metrics.PerClassLogLoss.Count() > 1 ? $"    * Class 2: {metrics.PerClassLogLoss[1]:0.####}" : "")}
        {(metrics.PerClassLogLoss.Count() > 2 ? $"    * Class 3: {metrics.PerClassLogLoss[2]:0.####}" : "")}
        """);

    await action.Summary.WriteAsync();

    action.WriteInfo($"************************************************************");
    action.WriteInfo($"MacroAccuracy = {metrics.MacroAccuracy:0.####}, a value between 0 and 1, the closer to 1, the better");
    action.WriteInfo($"MicroAccuracy = {metrics.MicroAccuracy:0.####}, a value between 0 and 1, the closer to 1, the better");
    action.WriteInfo($"LogLoss = {metrics.LogLoss:0.####}, the closer to 0, the better");

    if (metrics.PerClassLogLoss.Count() > 0)
        action.WriteInfo($"LogLoss for class 1 = {metrics.PerClassLogLoss[0]:0.####}, the closer to 0, the better");

    if (metrics.PerClassLogLoss.Count() > 1)
        action.WriteInfo($"LogLoss for class 2 = {metrics.PerClassLogLoss[1]:0.####}, the closer to 0, the better");

    if (metrics.PerClassLogLoss.Count() > 2)
        action.WriteInfo($"LogLoss for class 3 = {metrics.PerClassLogLoss[2]:0.####}, the closer to 0, the better");

    action.WriteInfo($"************************************************************");

    action.WriteInfo($"Saving model to '{modelPath}'...");
    EnsureOutputDirectory(modelPath);
    mlContext.Model.Save(trainedModel, split.TrainSet.Schema, modelPath);
}
