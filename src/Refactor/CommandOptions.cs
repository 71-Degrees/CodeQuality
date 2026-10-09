/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Globalization;

namespace CodeQuality.Refactor;

/// <summary>Reads <c>--name value</c> pairs and <c>--flag</c> switches from a command line.</summary>
internal sealed class CommandOptions
{
    private readonly string[] _args;

    public CommandOptions(string[] args) => _args = args;

    public bool Flag(string name) => _args.Contains(name);

    public bool Has(string name) => Array.IndexOf(_args, name) >= 0;

    public string Text(string name, string fallback = "")
    {
        var index = Array.IndexOf(_args, name);
        return index >= 0 && index + 1 < _args.Length ? _args[index + 1] : fallback;
    }

    public int Int(string name) => int.Parse(Text(name, "0"), CultureInfo.InvariantCulture);

    /// <summary>The first of these options that was not given, or null when all were.</summary>
    public string? Required(params string[] names) => names.FirstOrDefault(n => !Has(n));
}
