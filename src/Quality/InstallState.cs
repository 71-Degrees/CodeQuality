/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeQuality.Quality;

/// <summary>What the published copy was built from: the CodeQuality commit, when, and the versions of the script tools.</summary>
internal sealed record InstallState(
    string Commit,
    DateTimeOffset InstalledAt,
    string? TypeScript,
    [property: JsonPropertyName("eslint")] string? ESLint)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Null when there is no state file. An unreadable state counts as not installed, so the next install rewrites it.</summary>
    public static InstallState? Read(string file)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallState>(File.ReadAllText(file), Json);
        }
        catch (JsonException)
        {
            // A damaged file is treated as not installed; the next install writes a fresh one.
            return null;
        }
        catch (IOException)
        {
            // A file locked by another process is treated as not installed rather than failing the check.
            return null;
        }
    }

    public void Write(string file) => File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
}
