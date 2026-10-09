/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Refactor;

namespace CodeQuality.Refactor.Tests;

public class EmptyCatchLoggerTests
{
    private static string Wrap(string body, string members = "private readonly ILogger<Svc> _logger;") =>
        "class Svc\n{\n    " + members + "\n\n    async Task Run()\n    {\n        try\n        {\n            await Work();\n        }\n" + body + "    }\n}\n";

    [Fact]
    public void A_cancellation_is_logged_as_a_stop_and_keeps_its_comment()
    {
        var result = EmptyCatchLogger.Apply(Wrap("        catch (OperationCanceledException)\n        {\n            // App is shutting down.\n        }\n"));

        Assert.Contains("// App is shutting down.\n            _logger.LogDebug(\"Svc.Run stopped: cancelled\");", result.Text);
        Assert.Equal(new[] { "Svc.Run" }, result.Logged);
    }

    [Fact]
    public void Another_exception_is_logged_with_itself_and_a_missing_variable_is_added()
    {
        var result = EmptyCatchLogger.Apply(Wrap("        catch (WebSocketException)\n        {\n        }\n"));

        Assert.Contains("catch (WebSocketException ex)", result.Text);
        Assert.Contains("_logger.LogDebug(ex, \"Svc.Run ignored a WebSocketException\");", result.Text);
    }

    [Fact]
    public void A_bare_catch_becomes_a_catch_of_exception_with_a_variable()
    {
        var result = EmptyCatchLogger.Apply(Wrap("        catch\n        {\n        }\n"));

        Assert.Contains("catch (Exception ex)", result.Text);
        Assert.Contains("_logger.LogDebug(ex, \"Svc.Run ignored a Exception\");", result.Text);
    }

    [Fact]
    public void A_variable_the_catch_already_has_is_kept()
    {
        var result = EmptyCatchLogger.Apply(Wrap("        catch (IOException problem)\n        {\n        }\n"));

        Assert.Contains("_logger.LogDebug(problem, ", result.Text);
        Assert.DoesNotContain("problem ex", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_catch_with_a_statement_in_it_is_not_touched()
    {
        var source = Wrap("        catch (IOException)\n        {\n            return;\n        }\n");

        Assert.Equal(source, EmptyCatchLogger.Apply(source).Text);
    }

    [Fact]
    public void A_type_with_no_logger_is_left_for_a_person()
    {
        var source = Wrap("        catch (IOException)\n        {\n        }\n", members: "private int _count;");
        var result = EmptyCatchLogger.Apply(source);

        Assert.Equal(source, result.Text);
        Assert.Equal(new[] { "Svc.Run" }, result.Skipped);
    }

    [Fact]
    public void A_primary_constructor_logger_is_used()
    {
        var result = EmptyCatchLogger.Apply("class Svc(ILogger<Svc> log)\n{\n    void Run()\n    {\n        try { }\n        catch (IOException)\n        {\n        }\n    }\n}\n");

        Assert.Contains("log.LogDebug(ex, ", result.Text);
    }

    [Fact]
    public void An_instance_logger_is_not_used_from_a_static_method()
    {
        var result = EmptyCatchLogger.Apply("class Svc\n{\n    private readonly ILogger _logger;\n\n    static void Run()\n    {\n        try { }\n        catch (IOException)\n        {\n        }\n    }\n}\n");

        Assert.Single(result.Skipped);
    }

    [Fact]
    public void Several_catches_in_one_file_are_all_logged_without_disturbing_each_other()
    {
        var source = "class Svc\n{\n    private readonly ILogger _logger;\n\n    void A()\n    {\n        try { }\n        catch (IOException)\n        {\n        }\n    }\n\n    void B()\n    {\n        try { }\n        catch (OperationCanceledException)\n        {\n        }\n    }\n}\n";
        var result = EmptyCatchLogger.Apply(source);

        Assert.Equal(new[] { "Svc.A", "Svc.B" }, result.Logged);
        Assert.Contains("\"Svc.A ignored a IOException\"", result.Text);
        Assert.Contains("\"Svc.B stopped: cancelled\"", result.Text);
    }
}
