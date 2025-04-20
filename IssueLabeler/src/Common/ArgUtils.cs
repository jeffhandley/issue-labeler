// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

public static class ArgUtils
{
    public static string? GetString(string inputName, Func<string, string?> getInput)
    {
        string? input = getInput(inputName);
        return string.IsNullOrWhiteSpace(input) ? null : input;
    }

    public static bool GetFlag(string inputName, Func<string, string?> getInput, [NotNullWhen(true)] out bool? value, Action<string> showUsage)
    {
        string? input = GetString(inputName, getInput);

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

    public static bool TryGetRequiredString(string inputName, Func<string, string?> getInput, [NotNullWhen(true)] out string? value, Action<string> showUsage)
    {
        value = GetString(inputName, getInput);

        if (value is null)
        {
            showUsage($"Input '{inputName}' has an empty value.");
            return false;
        }

        return true;
    }

    public static bool TryDequeueString(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out string? argValue) =>
        TryGetRequiredString(argName, _ => Dequeue(args), out argValue, showUsage);

    public static bool TryParseRepo(string inputName, Func<string, string?> getInput, [NotNullWhen(true)] out string? org, [NotNullWhen(true)] out string? repo, Action<string> showUsage)
    {
        string? orgRepo = GetString(inputName, getInput);

        if (orgRepo is null)
        {
            orgRepo = GetString("GITHUB_REPOSITORY", Environment.GetEnvironmentVariable);
        }

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

    public static bool TryDequeueRepo(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out string? org, [NotNullWhen(true)] out string? repo) =>
        TryParseRepo(argName, _ => Dequeue(args), out org, out repo, showUsage);

    public static bool TryDequeueRepoList(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out string? org, [NotNullWhen(true)] out List<string>? repos)
    {
        string? orgRepos = ArgUtils.Dequeue(args);
        org = null;
        repos = null;

        if (orgRepos is null)
        {
            showUsage($$"""Input '{{argName}}' has an empty value or is not in the format of '{org}/{repo}': {{orgRepos}}""");
            return false;
        }

        foreach (var orgRepo in orgRepos.Split(',').Select(r => r.Trim()))
        {
            if (!orgRepo.Contains('/'))
            {
                showUsage($$"""Input '{{argName}}' contains a value that is not in the format of '{org}/{repo}': {{orgRepo}}""");
                return false;
            }

            string[] parts = orgRepo.Split('/');

            if (org is not null && org != parts[0])
            {
                showUsage($"All '{argName}' values must be from the same org.");
                return false;
            }

            org ??= parts[0];
            repos ??= [];
            repos.Add(parts[1]);
        }

        return (org is not null && repos is not null);
    }

    public static bool TryParseLabelPrefix(string inputName, Func<string, string?> getInput, [NotNullWhen(true)] out Func<string, bool>? labelPredicate, Action<string> showUsage)
    {
        string? labelPrefix = GetString(inputName, getInput);

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

    public static bool TryDequeueLabelPrefix(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out Func<string, bool>? labelPredicate) =>
        TryParseLabelPrefix(argName, _ => Dequeue(args), out labelPredicate, showUsage);

    public static bool TryParsePath(string inputName, Func<string, string?> getInput, out string? path)
    {
        path = GetString(inputName, getInput);

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

    public static bool TryDequeuePath(Queue<string> args, string argName, out string? path) =>
        TryParsePath(argName, _ => Dequeue(args), out path);

    public static bool TryParseStringArray(string inputName, Func<string, string?> getInput, [NotNullWhen(true)] out string[]? values)
    {
        string? input = GetString(inputName, getInput);

        if (input is null)
        {
            values = null;
            return false;
        }

        values = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return true;
    }

    public static bool TryDequeueStringArray(Queue<string> args, string argName, [NotNullWhen(true)] out string[]? argValues) =>
        TryParseStringArray(argName, _ => Dequeue(args), out argValues);

    public static bool TryParseInt(string inputName, Func<string, string?> getInput, [NotNullWhen(true)] out int? value, Action<string> showUsage)
    {
        string? input = GetString(inputName, getInput);

        if (input is null || !int.TryParse(input, out int parsedValue))
        {
            showUsage($"Input '{inputName}' must be an integer.");
            value = null;
            return false;
        }

        value = parsedValue;
        return true;
    }

    public static bool TryDequeueInt(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out int? argValue) =>
        TryParseInt(argName, _ => Dequeue(args), out argValue, showUsage);

    public static bool TryParseIntArray(string inputName, Func<string, string?> getInput, [NotNullWhen(true)] out int[]? values, Action<string> showUsage)
    {
        string? input = GetString(inputName, getInput);

        if (input is not null)
        {
            string[] inputValues = input.Split(',');

            int[] parsedValues = inputValues.SelectMany(v => {
                if (!TryParseInt(inputName, _ => v, out int? value, showUsage))
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

    public static bool TryDequeueIntArray(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out int[]? argValues) =>
        TryParseIntArray(argName, _ => Dequeue(args), out argValues, showUsage);

    public static bool TryParseFloat(string inputName, Func<string, string?> getInput, [NotNullWhen(true)] out float? value, Action<string> showUsage)
    {
        string? input = GetString(inputName, getInput);

        if (input is null || !float.TryParse(input, out float parsedValue))
        {
            showUsage($"Input '{inputName}' must be a decimal value.");
            value = null;
            return false;
        }

        value = parsedValue;
        return true;
    }

    public static bool TryDequeueFloat(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out float? argValue) =>
        TryParseFloat(argName, _ => Dequeue(args), out argValue, showUsage);

    public static bool TryParseNumberRanges(string inputName, Func<string, string?> getInput, [NotNullWhen(true)] out List<ulong>? values, Action<string> showUsage)
    {
        string? input = GetString(inputName, getInput);

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

    public static bool TryDequeueNumberRanges(Queue<string> args, Action<string> showUsage, string argName, out List<ulong>? argValues) =>
        TryParseNumberRanges(argName, _ => Dequeue(args), out argValues, showUsage);

    public static string? Dequeue(Queue<string> args)
    {
        if (args.TryDequeue(out string? argValue))
        {
            return string.IsNullOrWhiteSpace(argValue) ? null : argValue;
        }

        return null;
    }
}
