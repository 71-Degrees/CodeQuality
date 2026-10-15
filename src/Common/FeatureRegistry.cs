/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;

namespace CodeQuality.Common;

/// <summary>One thing a person can do with the site, as the owner would describe it.</summary>
public sealed class FeatureFunction
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Area { get; init; } = string.Empty;

    /// <summary>Who can do it: Anyone, Player, Subscriber, Admin, or System for things that happen without a person.</summary>
    public string Who { get; init; } = string.Empty;

    /// <summary>What it takes from the home page, in order. A step starting with "Click" is a click; any other is an entry or an act of the user's own.</summary>
    public string[] Steps { get; init; } = [];

    /// <summary>The pages it uses (route templates exactly as the site declares them).</summary>
    public string[] Routes { get; init; } = [];

    /// <summary>True only when a real browser test has driven the whole function; everything else is read from the code.</summary>
    public bool Walked { get; init; }

    /// <summary>Test class names, or <c>Class.Method</c> for one test. A name that matches nothing is reported, never silently ignored.</summary>
    public string[] Tests { get; init; } = [];

    /// <summary>Docs that explain it (file names in the docs folder), on top of the ones that name its pages.</summary>
    public string[] Docs { get; init; } = [];

    public string? Notes { get; init; }

    /// <summary>True for an entry <c>quality features --generate</c> drafted from the code and nobody has reviewed yet; a person clears it (or deletes the property) once the name, steps and notes are right.</summary>
    public bool Generated { get; init; }
}

/// <summary>The curated list of functions (<c>quality/features.json</c>), and the pages that are deliberately not a function (the error page).</summary>
public sealed class FeatureRegistryFile
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public string[] NotFunctions { get; init; } = [];

    public FeatureFunction[] Functions { get; init; } = [];

    /// <summary>The list as written in <paramref name="json"/> (comments and trailing commas allowed, as in the file).</summary>
    public static FeatureRegistryFile? Parse(string json) => JsonSerializer.Deserialize<FeatureRegistryFile>(json, Json);

    public static FeatureRegistryFile? Load(string root, FeatureSettings? settings)
    {
        var path = Path.Combine(root, settings?.Registry ?? "quality/features.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }
}
