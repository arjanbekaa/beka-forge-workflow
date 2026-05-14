using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Validates an operation request for safety before execution.
///
/// Checks:
///   - Operation exists in the manifest
///   - Required parameters are present
///   - Actor is suitable for the operation
///   - Phase exists (if targeting a specific phase)
///   - Access level is appropriate
///   - Append-only integrity is preserved
///
/// Returns safer alternatives when the requested operation is invalid or unsafe.
/// This handler is side-effect free — it writes nothing.
/// </summary>
public sealed class ValidateOperationRequestHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ValidateOperationRequest;

    public OperationResult Execute(OperationContext context)
    {
        var targetOperation = context.GetString("targetOperation");
        if (string.IsNullOrWhiteSpace(targetOperation))
            return OperationResult.Fail("ValidationFailed",
                "Parameter 'targetOperation' is required. Specify the operation name to validate.");

        var issues = new List<ValidationIssue>();
        if (IsPotentiallyUnsafeRaw(targetOperation))
        {
            issues.Add(ValidationIssue.Error(
                "UnsafeRawOperation",
                $"'{targetOperation}' appears to bypass WorkflowKit operation handlers. " +
                "Use safe operation names from the manifest instead.",
                alternatives: OperationManifestCatalog.GetWriteCapableEntries()
                    .Select(e => e.OperationName).ToArray()));

            return OperationResult.Ok(new OperationValidationResult
            {
                IsValid         = false,
                OperationName   = targetOperation,
                AccessLevel     = OperationAccessLevel.Read,
                Issues          = issues,
                ProposedActor   = context.Actor.ToString(),
                ProposedPhaseId = context.PhaseId
            });
        }

        var manifestEntry = OperationManifestCatalog.Find(targetOperation);

        // ── Check 1: Operation exists ──────────────────────────────────────────
        if (manifestEntry is null)
        {
            issues.Add(ValidationIssue.Error(
                "UnknownOperation",
                $"Operation '{targetOperation}' is not registered in the operation manifest. " +
                $"Use workflow.search_operations to find valid operations."));

            // Suggest read operations as safer alternatives
            var safeReadOps = OperationManifestCatalog.GetAll()
                .Where(e => e.AccessLevel == OperationAccessLevel.Read)
                .Select(e => e.OperationName)
                .Take(5).ToArray();
            issues[0] = issues[0] with { SaferAlternatives = safeReadOps };

            return OperationResult.Ok(new OperationValidationResult
            {
                IsValid        = false,
                OperationName  = targetOperation,
                AccessLevel    = OperationAccessLevel.Read,
                Issues         = issues,
                ProposedActor  = context.Actor.ToString(),
                ProposedPhaseId = context.PhaseId
            });
        }

        // ── Check 2: Required parameters (from the caller's parameters) ─────────
        var requiredParams = GetRequiredParameters(manifestEntry);
        var missingParams = new List<string>();
        if (requiredParams is { Length: > 0 })
        {
            foreach (var param in requiredParams)
            {
                // Skip "phaseId" — that's checked separately since it comes from context.PhaseId
                if (string.Equals(param, "phaseId", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = context.GetString(param);
                if (string.IsNullOrWhiteSpace(value))
                    missingParams.Add(param);
            }

            if (missingParams.Count > 0)
            {
                issues.Add(ValidationIssue.Error(
                    "MissingRequiredParameter",
                    $"Missing required parameters: {string.Join(", ", missingParams)}.",
                    field: missingParams[0]));
            }
        }

        // ── Check 3: Phase existence ───────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(context.PhaseId))
        {
            // Check if this operation type requires a phase target
            var phaseRequiredOps = new[]
            {
                WorkflowOperations.CreateAuditLog, WorkflowOperations.CreateFixLog,
                WorkflowOperations.CreateImplementationLog, WorkflowOperations.CreateReviewLog,
                WorkflowOperations.CreateTestLog, WorkflowOperations.RecordBlocker,
                WorkflowOperations.ResolveBlocker, WorkflowOperations.CreateHandoff,
                WorkflowOperations.GetHandoffs, WorkflowOperations.RecordTimeSpent,
                WorkflowOperations.GetPhaseContract, WorkflowOperations.SavePhaseContract,
                WorkflowOperations.UpdatePhase, WorkflowOperations.UpdatePhaseStatus,
                WorkflowOperations.AssignPhase, WorkflowOperations.StartPhase,
                WorkflowOperations.CompleteImplementation
            };

            var phase = store.LoadPhase(context.PhaseId);
            if (phase is null)
            {
                issues.Add(ValidationIssue.Warning(
                    "PhaseNotFound",
                    $"Phase '{context.PhaseId}' was not found. Ensure the phase exists before calling this operation.",
                    field: "phaseId"));
            }
        }
        else
        {
            // Some operations require phaseId
            var phaseStrictOps = new[]
            {
                WorkflowOperations.CreateAuditLog, WorkflowOperations.CreateFixLog,
                WorkflowOperations.CreateImplementationLog, WorkflowOperations.CreateReviewLog,
                WorkflowOperations.CreateTestLog, WorkflowOperations.RecordBlocker,
                WorkflowOperations.ResolveBlocker, WorkflowOperations.CreateHandoff,
                WorkflowOperations.StartPhase, WorkflowOperations.CompleteImplementation
            };

            if (phaseStrictOps.Contains(targetOperation, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(ValidationIssue.Error(
                    "MissingPhaseId",
                    $"Operation '{targetOperation}' requires a phaseId. Provide a phase ID in the request context.",
                    field: "phaseId"));
            }
        }

        // ── Check 4: Actor suitability ─────────────────────────────────────────
        var suitableActors = GetSuitableActors(manifestEntry);
        if (suitableActors is { Length: > 0 })
        {
            var actorName = context.Actor.ToString().ToLowerInvariant();
            var matches = suitableActors.Any(a =>
                string.Equals(a, actorName, StringComparison.OrdinalIgnoreCase));

            if (!matches)
            {
                issues.Add(ValidationIssue.Warning(
                    "UnsuitableActor",
                    $"Actor '{context.Actor}' may not be suitable for '{targetOperation}'. " +
                    $"Suitable actors: {string.Join(", ", suitableActors)}.",
                    field: "actor",
                    alternatives: suitableActors));
            }
        }

        // ── Check 5: Append-only integrity ─────────────────────────────────────
        if (manifestEntry.AccessLevel == OperationAccessLevel.Append)
        {
            // Append operations are always safe — they never overwrite history.
            // No error, but we add an advisory note.
        }
        else if (manifestEntry.AccessLevel == OperationAccessLevel.Write)
        {
            // Write operations should be approached with care.
            issues.Add(ValidationIssue.Warning(
                "WriteOperation",
                $"'{targetOperation}' is a Write operation. It modifies authoritative JSON state files. " +
                "Verify the current state with a read operation before writing.",
                field: "targetOperation"));
        }

        // ── Check 6: Raw file operations ───────────────────────────────────────
        // Any operation that looks like it bypasses the handler model.
        if (IsPotentiallyUnsafeRaw(targetOperation))
        {
            issues.Add(ValidationIssue.Error(
                "UnsafeRawOperation",
                $"'{targetOperation}' appears to bypass WorkflowKit operation handlers. " +
                "Use safe operation names from the manifest instead.",
                alternatives: OperationManifestCatalog.GetWriteCapableEntries()
                    .Select(e => e.OperationName).ToArray()));
        }

        // ── Build safer alternatives for errors ────────────────────────────────
        var errorIssues = issues.Where(i => i.Severity == "error" && i.SaferAlternatives is null).ToList();
        if (errorIssues.Count > 0)
        {
            // Collect safe read operations as alternatives
            var safeReadOps = OperationManifestCatalog.GetAll()
                .Where(e => e.AccessLevel == OperationAccessLevel.Read)
                .Select(e => e.OperationName)
                .Take(5).ToArray();

            foreach (var issue in errorIssues)
            {
                // Replace the issue with one that has alternatives
                var idx = issues.IndexOf(issue);
                if (idx >= 0)
                {
                    issues[idx] = issue with { SaferAlternatives = safeReadOps };
                }
            }
        }

        var result = new OperationValidationResult
        {
            IsValid          = !issues.Any(i => i.Severity == "error"),
            OperationName    = targetOperation,
            AccessLevel      = manifestEntry.AccessLevel,
            Issues           = issues,
            RequiredParameters = requiredParams,
            WriteTargets     = manifestEntry.WriteTargets,
            ProposedActor    = context.Actor.ToString(),
            ProposedPhaseId  = context.PhaseId
        };

        return OperationResult.Ok(result);
    }

    /// <summary>
    /// Extracts required parameter names from the manifest entry's WriteTargets.
    /// Falls back to common conventions if no WriteTargets are defined.
    /// </summary>
    private static string[]? GetRequiredParameters(OperationManifestEntry entry)
    {
        if (entry.WriteTargets is { Count: > 0 })
        {
            // Collect unique required parameters across all write targets
            var allParams = entry.WriteTargets
                .Where(wt => wt.RequiredParameters is { Length: > 0 })
                .SelectMany(wt => wt.RequiredParameters!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (allParams.Length > 0)
                return allParams;
        }

        // Fallback: common required parameters by access level
        if (entry.AccessLevel == OperationAccessLevel.Append)
            return ["phaseId", "summary"];

        if (entry.AccessLevel == OperationAccessLevel.Write)
            return ["phaseId"];

        return null;
    }

    /// <summary>
    /// Extracts suitable actor names from the manifest entry's WriteTargets.
    /// </summary>
    private static string[]? GetSuitableActors(OperationManifestEntry entry)
    {
        if (entry.WriteTargets is { Count: > 0 })
        {
            return entry.WriteTargets
                .Where(wt => wt.SuitableActors is { Length: > 0 })
                .SelectMany(wt => wt.SuitableActors!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return null;
    }

    /// <summary>
    /// Detects operations that look like raw file/system access rather than
    /// safe WorkflowKit operations.
    /// </summary>
    private static bool IsPotentiallyUnsafeRaw(string operationName)
    {
        // Any operation not starting with "workflow." or "bekaforge." is suspect.
        if (!operationName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase) &&
            !operationName.StartsWith("bekaforge.", StringComparison.OrdinalIgnoreCase))
            return true;

        // File-system-level operations are unsafe.
        var unsafePrefixes = new[]
        {
            "file.", "fs.", "io.", "os.", "shell.", "cmd.",
            "system.", "process.", "http.", "network."
        };

        return unsafePrefixes.Any(p =>
            operationName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
