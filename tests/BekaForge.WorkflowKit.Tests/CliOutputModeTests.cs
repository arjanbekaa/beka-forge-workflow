using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Server.Handlers;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for PHASE-022: Rich Terminal Output.
/// Covers output mode selection logic, JSON cleanliness invariant, and
/// plain fallback. Does not test visual rendering fidelity (snapshot tests
/// for Spectre.Console output are fragile across terminal environments).
/// </summary>
public sealed class CliOutputModeTests
{
    // ── Mode resolution ─────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_JsonFlagTrue_ReturnsJson()
    {
        var mode = CliRenderer.Resolve(jsonFlag: true, plainFlag: false);
        Assert.Equal(CliOutputMode.Json, mode);
    }

    [Fact]
    public void Resolve_JsonFlagBeatsPlainFlag()
    {
        // --json beats --plain when both are set
        var mode = CliRenderer.Resolve(jsonFlag: true, plainFlag: true);
        Assert.Equal(CliOutputMode.Json, mode);
    }

    [Fact]
    public void Resolve_PlainFlagTrue_ReturnsPlain()
    {
        var mode = CliRenderer.Resolve(jsonFlag: false, plainFlag: true);
        Assert.Equal(CliOutputMode.Plain, mode);
    }

    [Fact]
    public void Resolve_NoFlags_ReturnsRichOrPlain_NeverJson()
    {
        // When no flags are set the result is Rich or Plain depending on ANSI
        // detection — but it must never be Json.
        var mode = CliRenderer.Resolve(jsonFlag: false, plainFlag: false);
        Assert.NotEqual(CliOutputMode.Json, mode);
    }

    // ── JSON cleanliness ────────────────────────────────────────────────────────

    [Fact]
    public void PhaseStateMarkup_JsonMode_ReturnsPlainText()
    {
        // When rendering for JSON we must never embed markup in the output.
        // PhaseStateMarkup is only called from rich rendering paths, but we
        // verify that the plain-mode helpers used in Json paths produce no
        // ANSI escape sequences or Spectre markup brackets.
        var plain = CliRenderer.PlainHealthIcon(true);
        Assert.DoesNotContain("\x1b[", plain);     // no ANSI ESC sequences
        Assert.DoesNotContain("[green]", plain);   // no Spectre markup
        Assert.DoesNotContain("[/]", plain);

        var plainFail = CliRenderer.PlainHealthIcon(false);
        Assert.DoesNotContain("\x1b[", plainFail);
        Assert.DoesNotContain("[red]", plainFail);
    }

    // ── Phase state colour mapping ──────────────────────────────────────────────

    [Theory]
    [InlineData("pass",         "[bold green]")]
    [InlineData("auditlogged",  "[bold cyan]")]
    [InlineData("inprogress",   "[bold yellow]")]
    [InlineData("planned",      "[grey]")]
    [InlineData("failed",       "[bold red]")]
    [InlineData("blocked",      "[bold red]")]
    public void PhaseStateMarkup_KnownStates_ContainExpectedColour(string state, string expectedMarkup)
    {
        var markup = CliRenderer.PhaseStateMarkup(state);
        Assert.Contains(expectedMarkup, markup);
    }

    [Fact]
    public void PhaseStateMarkup_UnknownState_ReturnsEscapedText()
    {
        // Unknown states must be escaped — no raw markup injection
        var markup = CliRenderer.PhaseStateMarkup("some-new-state");
        Assert.Contains("some-new-state", markup);
        // Must not produce unescaped brackets that could break Spectre rendering
        Assert.DoesNotContain("[some-new-state]", markup);
    }

    [Fact]
    public void PhaseStateMarkup_NullState_DoesNotThrow()
    {
        var markup = CliRenderer.PhaseStateMarkup(null);
        Assert.NotNull(markup);
    }

    [Fact]
    public void RenderPhaseCard_Plain_IncludesProgress_AndSubPhases()
    {
        using var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            CliRenderer.RenderPhaseCard(
                "PHASE-022",
                "Rich Terminal Output",
                "Planned",
                45,
                "Wire status and trace views.",
                ["PHASE-020", "PHASE-021"],
                [("PHASE-022-A", "Terminal Library Decision", "Completed")],
                1,
                CliOutputMode.Plain);
        }
        finally
        {
            Console.SetOut(original);
        }

        var text = writer.ToString();
        Assert.Contains("Progress:   45%", text);
        Assert.Contains("Sub-phases:", text);
        Assert.DoesNotContain("[bold]", text);
        Assert.DoesNotContain("\x1b[", text);
    }

    [Fact]
    public void RenderContext_Plain_DoesNotEmitMarkup()
    {
        using var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            CliRenderer.RenderContext(
                new BekaForge.WorkflowKit.AgentContracts.RelevantContextResult
                {
                    PhaseId = "PHASE-022",
                    Pointers =
                    [
                        new BekaForge.WorkflowKit.AgentContracts.ContextPointer
                        {
                            PointerType = "file",
                            Target = "src/BekaForge.WorkflowKit.Cli/Program.cs",
                            RelevanceScore = 0.95,
                            Reason = "Primary CLI entry point.",
                            EstimatedTokens = 120,}
                    ],
                    OmittedCandidates = 0,
                    EstimatedTotalTokens = 120,
                    Warnings = ["Do not scan the full workflow folder."],
                    IsFromCache = false
                },
                CliOutputMode.Plain);
        }
        finally
        {
            Console.SetOut(original);
        }

        var text = writer.ToString();
        Assert.Contains("Context Pointers for PHASE-022", text);
        Assert.Contains("Primary CLI entry point.", text);
        Assert.DoesNotContain("[yellow]", text);
        Assert.DoesNotContain("\x1b[", text);
    }

    [Fact]
    public void RenderTimeline_Plain_DoesNotEmitMarkup()
    {
        using var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            CliRenderer.RenderTimeline(
                [
                    new TimelineEntry
                    {
                        Source = "workflow",
                        EventType = "implementation",
                        PhaseId = "PHASE-022",
                        Actor = "Codex",
                        Summary = "Added rich renderer coverage."
                    }
                ],
                1,
                CliOutputMode.Plain);
        }
        finally
        {
            Console.SetOut(original);
        }

        var text = writer.ToString();
        Assert.Contains("Timeline (1 event(s))", text);
        Assert.Contains("Added rich renderer coverage.", text);
        Assert.DoesNotContain("[green]", text);
        Assert.DoesNotContain("\x1b[", text);
    }
}
