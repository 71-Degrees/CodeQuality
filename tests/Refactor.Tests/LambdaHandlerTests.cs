/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Refactor;

namespace CodeQuality.Refactor.Tests;

public class LambdaHandlerTests
{
    private const string Source =
        "static class E\n{\n    public static void Map(this WebApplication app, string key)\n    {\n        app.MapPost(\"/api/quiz-requests/{id}/claim\", async (string id, HttpContext http) =>\n        {\n            var ok = await Run(id);\n            return ok ? Results.Ok() : Results.Conflict();\n        })\n        .DisableAntiforgery();\n\n        app.MapGet(\"/version\", (SiteInfo site) => Results.Json(site));\n    }\n}\n";

    [Theory]
    [InlineData("MapPost", "/api/quiz-requests/{id}/claim", true, "PostQuizRequestsByIdClaimAsync")]
    [InlineData("MapGet", "/version", false, "GetVersion")]
    [InlineData("MapGet", "/", false, "GetRoot")]
    [InlineData("MapGet", "/og/results/{sessionCode}.png", false, "GetOgResultsBySessionCodePng")]
    public void A_handler_is_named_after_its_verb_and_route(string verb, string route, bool isAsync, string expected) =>
        Assert.Equal(expected, RouteNamer.NameFor(verb, route, isAsync));

    [Fact]
    public void Each_lambda_becomes_a_method_after_the_registering_method_and_the_route_keeps_its_chain()
    {
        var result = LambdaHandlers.Convert(Source, instance: false);

        Assert.Equal(2, result.Converted.Count);
        Assert.Contains("app.MapPost(\"/api/quiz-requests/{id}/claim\", PostQuizRequestsByIdClaimAsync)\n        .DisableAntiforgery();", result.Text);
        Assert.Contains("app.MapGet(\"/version\", GetVersion);", result.Text);
        Assert.Contains("    private static async Task<IResult> PostQuizRequestsByIdClaimAsync(string id, HttpContext http)\n    {\n        var ok = await Run(id);\n        return ok ? Results.Ok() : Results.Conflict();\n    }", result.Text);
        Assert.Contains("    private static IResult GetVersion(SiteInfo site) =>\n        Results.Json(site);", result.Text);
        Assert.True(result.Text.IndexOf("private static async", StringComparison.Ordinal) > result.Text.IndexOf("GetVersion);", StringComparison.Ordinal), "handlers come after the registering method");
    }

    [Fact]
    public void An_instance_conversion_leaves_out_static()
    {
        var result = LambdaHandlers.Convert(Source, instance: true);

        Assert.Contains("    private async Task<IResult> PostQuizRequestsByIdClaimAsync(", result.Text);
        Assert.DoesNotContain("private static", result.Text);
    }

    [Fact]
    public void A_variable_of_the_registering_method_that_the_handler_uses_is_reported()
    {
        var source = "static class E\n{\n    public static void Map(this WebApplication app, string key)\n    {\n        var limiter = new Limiter();\n        app.MapGet(\"/x\", (HttpContext http) =>\n        {\n            return limiter.Check(key, http) ? Results.Ok() : Results.Forbid();\n        });\n    }\n}\n";

        var result = LambdaHandlers.Convert(source, instance: false);

        Assert.Equal(new[] { "key", "limiter" }, Assert.Single(result.Converted).Captures);
    }

    [Fact]
    public void A_name_the_handler_declares_itself_is_not_a_capture()
    {
        var source = "static class E\n{\n    public static void Map(this WebApplication app, string key)\n    {\n        app.MapGet(\"/x\", (string key) =>\n        {\n            var other = key;\n            return Results.Ok(other);\n        });\n    }\n}\n";

        Assert.Empty(Assert.Single(LambdaHandlers.Convert(source, instance: false).Converted).Captures);
    }

    [Fact]
    public void A_lambda_with_inferred_parameter_types_is_left_alone_and_reported()
    {
        var source = "static class E\n{\n    public static void Map(this WebApplication app)\n    {\n        app.MapGet(\"/x\", (a, b) => Results.Ok());\n    }\n}\n";

        var result = LambdaHandlers.Convert(source, instance: false);

        Assert.Empty(result.Converted);
        Assert.Single(result.Skipped);
        Assert.Equal(source, result.Text);
    }

    [Fact]
    public void Two_routes_that_would_get_the_same_name_get_different_ones()
    {
        var source = "static class E\n{\n    public static void Map(this WebApplication app)\n    {\n        app.MapGet(\"/x\", () => Results.Ok());\n        app.MapGet(\"/x\", () => Results.Ok());\n    }\n}\n";

        var names = LambdaHandlers.Convert(source, instance: false).Converted.Select(c => c.Name).ToList();

        Assert.Equal(new[] { "GetX", "GetX2" }, names);
    }

    [Fact]
    public void A_multi_line_string_inside_a_handler_keeps_its_text_when_the_body_moves_out()
    {
        var source = "static class E\n{\n    public static void Map(this WebApplication app)\n    {\n        app.MapGet(\"/x\", () =>\n        {\n            var s = @\"a\n  b\";\n            return Results.Text(s);\n        });\n    }\n}\n";

        var result = LambdaHandlers.Convert(source, instance: false);

        Assert.Contains("var s = @\"a\n  b\";", result.Text);
        Assert.Contains("\n        var s = @\"a", result.Text);
    }
}
