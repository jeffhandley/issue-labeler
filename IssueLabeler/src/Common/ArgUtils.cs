// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Actions.Core.Services;

public class ArgUtils
{
    private ICoreService action;
    private Action<string?> showUsage;
    private Queue<string>? arguments { get; }

    public ArgUtils(ICoreService action, Action<string?, ICoreService> showUsage)
    {
        this.action = action;
        this.showUsage = message => showUsage(message, action);
    }

    public ArgUtils(ICoreService action, Action<string?, ICoreService> showUsage, Queue<string> arguments) : this(action, showUsage)
    {
        this.arguments = arguments;
    }

    public string? GetInputString(string inputName)
    {
        string? input = action.GetInput(inputName);
        return string.IsNullOrWhiteSpace(input) ? null : input;
    }

    public string? DequeueString()
    {
        if (arguments?.TryDequeue(out string? argValue) ?? false)
        {
            return string.IsNullOrWhiteSpace(argValue) ? null : argValue;
        }

        return null;
    }

    public bool TryGetString(string inputName, [NotNullWhen(true)] out string? value)
    {
        value = GetInputString(inputName);
        return value is not null;
    }

    public bool TryGetFlag(string inputName, [NotNullWhen(true)] out bool? value)
    {
        string? input = GetInputString(inputName);

        if (input is null)
        {
            value = false;
            return true;
        }

        if (!bool.TryParse(input, out bool parsedValue))
        {
            showUsage($"Input '{inputName}' must be 'true', 'false', 'TRUE', or 'FALSE'.");
            value = null;
            return false;
        }

        value = parsedValue;
        return true;
    }

    public bool TryGetRepo(string inputName, [NotNullWhen(true)] out string? org, [NotNullWhen(true)] out string? repo) =>
        TryParseRepo(inputName, GetInputString(inputName), out org, out repo);

    public bool TryDequeueRepo(string inputName, [NotNullWhen(true)] out string? org, [NotNullWhen(true)] out string? repo) =>
        TryParseRepo(inputName, DequeueString(), out org, out repo);

    private bool TryParseRepo(string inputName, string? orgRepo, [NotNullWhen(true)] out string? org, [NotNullWhen(true)] out string? repo)
    {
        orgRepo ??= Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");

        if (orgRepo is null || !orgRepo.Contains('/'))
        {
            showUsage($$"""Input '{{inputName}}' has an empty value or is not in the format of '{org}/{repo}'. Value defaults to GITHUB_REPOSITORY environment variable if not specified.""");
            org = null;
            repo = null;
            return false;
        }

        string[] parts = orgRepo.Split('/');
        org = parts[0];
        repo = parts[1];
        return true;
    }

    public bool TryDequeueRepoList(string inputName, [NotNullWhen(true)] out string? org, [NotNullWhen(true)] out List<string>? repos)
    {
        string? orgRepos = DequeueString();
        org = null;
        repos = null;

        if (orgRepos is null)
        {
            showUsage($$"""Input '{{inputName}}' has an empty value or is not in the format of '{org}/{repo}': {{orgRepos}}""");
            return false;
        }

        foreach (var orgRepo in orgRepos.Split(',').Select(r => r.Trim()))
        {
            if (!orgRepo.Contains('/'))
            {
                showUsage($$"""Input '{{inputName}}' contains a value that is not in the format of '{org}/{repo}': {{orgRepo}}""");
                return false;
            }

            string[] parts = orgRepo.Split('/');

            if (org is not null && org != parts[0])
            {
                showUsage($"All '{inputName}' values must be from the same org.");
                return false;
            }

            org ??= parts[0];
            repos ??= [];
            repos.Add(parts[1]);
        }

        return (org is not null && repos is not null);
    }

    public bool TryGetLabelPrefix(string inputName, [NotNullWhen(true)] out Func<string, bool>? labelPredicate) =>
        TryParseLabelPrefix(inputName, GetInputString(inputName), out labelPredicate);

    public bool TryDequeueLabelPrefix(string inputName, [NotNullWhen(true)] out Func<string, bool>? labelPredicate) =>
        TryParseLabelPrefix(inputName, DequeueString(), out labelPredicate);

    private bool TryParseLabelPrefix(string inputName, string? labelPrefix, [NotNullWhen(true)] out Func<string, bool>? labelPredicate)
    {
        if (labelPrefix is null)
        {
            labelPredicate = null;
            return false;
        }

        // Require that the label prefix end in something other than a letter or number
        // This promotes the pattern of prefixes that are clear, rather than a prefix that
        // could be matched as the beginning of another word in the label
        if (Regex.IsMatch(labelPrefix.AsSpan(^1),"[a-zA-Z0-9]"))
        {
            showUsage($"""
                Input '{inputName}' must end in a non-alphanumeric character.

                The recommended label prefix terminating character is '-'.
                The recommended label prefix for applying area labels is 'area-'.
                """);

            labelPredicate = null;
            return false;
        }

        labelPredicate = (label) => label.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    public bool TryGetPath(string inputName, out string? path) =>
        TryParsePath(GetInputString(inputName), out path);

    public bool TryDequeuePath(string inputName, out string? path) =>
        TryParsePath(DequeueString(), out path);

    private bool TryParsePath(string? input, out string? path)
    {
        path = input;

        if (path is null)
        {
            return false;
        }

        if (!Path.IsPathRooted(path))
        {
            path = Path.GetFullPath(path);
        }

        return true;
    }

    public bool TryGetStringArray(string inputName, [NotNullWhen(true)] out string[]? values) =>
        TryParseStringArray(inputName, GetInputString(inputName), out values);

    public bool TryDequeueStringArray(string inputName, [NotNullWhen(true)] out string[]? values) =>
        TryParseStringArray(inputName, DequeueString(), out values);

    private bool TryParseStringArray(string inputName, string? input, [NotNullWhen(true)] out string[]? values)
    {
        if (input is null)
        {
            values = null;
            return false;
        }

        values = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return true;
    }

    public bool TryDequeueInt(string inputName, [NotNullWhen(true)] out int? value) =>
        TryParseInt(inputName, DequeueString(), out value);

    private bool TryParseInt(string inputName, string? input, [NotNullWhen(true)] out int? value)
    {
        if (input is null || !int.TryParse(input, out int parsedValue))
        {
            showUsage($"Input '{inputName}' must be an integer.");
            value = null;
            return false;
        }

        value = parsedValue;
        return true;
    }

    public bool TryGetIntArray(string inputName, [NotNullWhen(true)] out int[]? values) =>
        TryParseIntArray(inputName, GetInputString(inputName), out values);

    public bool TryDequeueIntArray(string inputName, [NotNullWhen(true)] out int[]? values) =>
        TryParseIntArray(inputName, DequeueString(), out values);

    private bool TryParseIntArray(string inputName, string? input, [NotNullWhen(true)] out int[]? values)
    {
        if (input is not null)
        {
            string[] inputValues = input.Split(',');

            int[] parsedValues = inputValues.SelectMany(v => {
                if (!TryParseInt(inputName, v, out int? value))
                {
                    return new int[0];
                }

                return [value.Value];
            }).ToArray();

            if (parsedValues.Length == inputValues.Length)
            {
                values = parsedValues;
                return true;
            }
        }

        values = null;
        return false;
    }

    public bool TryGetFloat(string inputName, [NotNullWhen(true)] out float? value) =>
        TryParseFloat(inputName, GetInputString(inputName), out value);

    public bool TryDequeueFloat(string inputName, [NotNullWhen(true)] out float? value) =>
        TryParseFloat(inputName, DequeueString(), out value);

    private bool TryParseFloat(string inputName, string? input, [NotNullWhen(true)] out float? value)
    {
        if (input is null || !float.TryParse(input, out float parsedValue))
        {
            showUsage($"Input '{inputName}' must be a decimal value.");
            value = null;
            return false;
        }

        value = parsedValue;
        return true;
    }

    public bool TryGetNumberRanges(string inputName, [NotNullWhen(true)] out List<ulong>? values) =>
        TryParseNumberRanges(inputName, GetInputString(inputName), out values);

    public bool TryDequeueNumberRanges(string inputName, [NotNullWhen(true)] out List<ulong>? values) =>
        TryParseNumberRanges(inputName, DequeueString(), out values);

    private bool TryParseNumberRanges(string inputName, string? input, [NotNullWhen(true)] out List<ulong>? values)
    {
        if (input is not null)
        {
            var showUsageError = () => showUsage($"Input '{inputName}' must be comma-separated list of numbers and/or dash-separated ranges. Example: 1-3,5,7-9.");
            List<ulong> numbers = [];

            foreach (var range in input.Split(','))
            {
                var beginEnd = range.Split('-');

                if (beginEnd.Length == 1)
                {
                    if (!ulong.TryParse(beginEnd[0], out ulong number))
                    {
                        showUsageError();
                        values = null;
                        return false;
                    }

                    numbers.Add(number);
                }
                else if (beginEnd.Length == 2)
                {
                    if (!ulong.TryParse(beginEnd[0], out ulong begin))
                    {
                        showUsageError();
                        values = null;
                        return false;
                    }

                    if (!ulong.TryParse(beginEnd[1], out ulong end))
                    {
                        showUsageError();
                        values = null;
                        return false;
                    }

                    for (var number = begin; number <= end; number++)
                    {
                        numbers.Add(number);
                    }
                }
                else
                {
                    showUsageError();
                    values = null;
                    return false;
                }
            }

            values = numbers;
            return true;
        }

        values = null;
        return false;
    }
}
