/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Common;

/// <summary>What the feature map needs to know about a site. Every setting is optional.</summary>
public sealed class FeatureSettings
{
    /// <summary>Route prefixes that need a signed-in visitor (the crawler cannot see them without logging in).</summary>
    public string[] SignedInPrefixes { get; init; } = [];
}
