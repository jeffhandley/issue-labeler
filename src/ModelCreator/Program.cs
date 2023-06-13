using Microsoft.ML;
using Microsoft.ML.AutoML;
using Microsoft.ML.Data;

var mlContext = new MLContext(seed: 0);

var experimentSettings = new MulticlassExperimentSettings();
experimentSettings.Trainers.Clear();
experimentSettings.Trainers.Add(MulticlassClassificationTrainer.SdcaMaximumEntropy);

var cts = new System.Threading.CancellationTokenSource();
experimentSettings.CancellationToken = cts.Token;
experimentSettings.CacheDirectoryName = Path.GetTempPath();
experimentSettings.OptimizingMetric = MulticlassClassificationMetric.MicroAccuracy;

var options = new TextLoader.Options
{
    Columns = new[]
    {
        new TextLoader.Column("ID", DataKind.Single, 0),
        new TextLoader.Column("Area", DataKind.String, 1),
        new TextLoader.Column("Title", DataKind.String, 2),
        new TextLoader.Column("Description", DataKind.String, 3)
    },
    Separators = new[] { '\t' }
};

var textLoader = mlContext.Data.CreateTextLoader(options);
var data = mlContext.Data.TrainTestSplit(textLoader.Load("repro.tsv"), seed: 0);

var columns = new ColumnInformation { LabelColumnName = "Area" };
columns.IgnoredColumnNames.Add("ID");
columns.IgnoredColumnNames.Add("Title");
columns.IgnoredColumnNames.Add("Description");

mlContext.Auto()
    .CreateMulticlassClassificationExperiment(experimentSettings)
    .Execute(data.TrainSet, columns);
