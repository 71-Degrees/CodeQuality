/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Xunit;

namespace CodeQuality.Browser;

/// <summary>A test that needs a real browser. It is skipped, with the reason, where Chromium is not installed or browser tests are switched off, so the suite stays green on a machine that cannot run them.</summary>
public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        Skip = Chromium.WhyUnavailable();
    }
}
