/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;

namespace CodeQuality.Quality;

/// <summary>Reads and writes the tool's own state files (the report's latest results, the timeline). An unreadable file just means starting afresh, so a half-written file never stops a run.</summary>
internal static class JsonState
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T Load<T>(string path)
        where T : class, new()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T() : new T();
        }
        catch (JsonException)
        {
            return new T();   // an unreadable state file is rebuilt by the next run (the timeline with history --backfill)
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options) + Environment.NewLine);
    }
}
