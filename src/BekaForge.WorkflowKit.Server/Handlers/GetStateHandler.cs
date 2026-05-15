using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class GetStateHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetState;

    public OperationResult Execute(OperationContext context)
    {
        var state = store.LoadWorkflow();
        return OperationResult.Ok(state);
    }
}

public sealed class GetCurrentPhaseHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetCurrentPhase;

    public OperationResult Execute(OperationContext context)
    {
        var state = store.LoadWorkflow();
        if (state.CurrentPhaseId is null)
            return OperationResult.Ok(null);

        var phase = store.LoadPhase(state.CurrentPhaseId);
        return OperationResult.Ok(phase);
    }
}

public sealed class ListPhasesHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ListPhases;

    public OperationResult Execute(OperationContext context)
    {
        var phases = store.LoadAllPhases();
        return OperationResult.Ok(phases);
    }
}

public sealed class ValidateStateHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ValidateState;

    public OperationResult Execute(OperationContext context)
    {
        var state = store.LoadWorkflow();
        var phases = store.LoadAllPhases();
        var validations = store.ReadAllValidations();
        var tests = store.ReadAllTests();

        var issues = new List<string>();

        // -- Basic structural checks -----------------------------------------

        // All phase IDs in workflow.json must have corresponding files.
        foreach (var phaseId in state.PhaseIds)
        {
            if (!store.PhaseExists(phaseId))
                issues.Add($"Phase {phaseId} is listed in workflow.json but has no phase file.");
        }

        // Current phase ID must be in the phase list.
        if (state.CurrentPhaseId is not null && !state.PhaseIds.Contains(state.CurrentPhaseId))
            issues.Add($"CurrentPhaseId '{state.CurrentPhaseId}' is not in the phaseIds list.");

        // -- Validation honesty doctor checks --------------------------------

        // Check 1: Legacy test records with passed=true but no evidence (legacy test.jsonl)
        foreach (var test in tests)
        {
            if (test.Passed)
                issues.Add(
                    $"[WARNING] Legacy test record {test.TestId} (phase {test.PhaseId}) " +
                    "is marked as passed with no evidence. " +
                    "Consider re-logging with workflow.create_validation_log.");
        }

        // Check 2: Manual/browser validation marked Passed without human evidence
        foreach (var val in validations)
        {
            bool isManualType = val.ValidationType is ValidationType.BrowserManual
                or ValidationType.UnityManual
                or ValidationType.HumanValidationRequired;

            bool isPassed = val.ValidationResult is ValidationResult.Passed
                or ValidationResult.PassedWithWarnings;

            if (isPassed && isManualType)
            {
                bool hasHumanEvidence = val.EvidenceItems.Any(e =>
                    e.Source == EvidenceSource.HumanOwner);

                if (!hasHumanEvidence)
                    issues.Add(
                        $"[ERROR] Validation {val.ValidationId} (phase {val.PhaseId}) " +
                        $"is marked as {val.ValidationResult} with type {val.ValidationType} " +
                        "but has no HumanOwner evidence. " +
                        "Manual validation types require human confirmation.");
            }

            // Check 3: Passed without evidence
            if (isPassed && val.EvidenceItems.Count == 0)
                issues.Add(
                    $"[ERROR] Validation {val.ValidationId} (phase {val.PhaseId}) " +
                    "is marked as passed but has no evidence items.");
        }

        // Check 4: Phases marked Pass with skipped validation but no reason
        foreach (var phase in phases)
        {
            if (phase.State is PhaseState.Pass or PhaseState.PassWithWarnings)
            {
                var phaseValidations = validations
                    .Where(v => string.Equals(v.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (phaseValidations.Count == 0)
                {
                    issues.Add(
                        $"[WARNING] Phase {phase.PhaseId} is in {phase.State} state " +
                        "but has no validation records.");
                }
                else
                {
                    var latest = phaseValidations.MaxBy(v => v.CreatedUtc);
                    if (latest is not null && latest.ValidationResult == ValidationResult.Skipped)
                    {
                        if (string.IsNullOrWhiteSpace(latest.SkipReason))
                            issues.Add(
                                $"[ERROR] Phase {phase.PhaseId} is in {phase.State} state " +
                                "with a skipped validation that has no skip reason.");
                    }
                }
            }

            // Check 5: Phases stuck at validation pending (ReadyForTest or TestInProgress with PendingUser)
            if (phase.State is PhaseState.ReadyForTest or PhaseState.TestInProgress)
            {
                var phaseValidations = validations
                    .Where(v => string.Equals(v.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (phaseValidations.Count > 0)
                {
                    var latest = phaseValidations.MaxBy(v => v.CreatedUtc);
                    if (latest is not null && latest.ValidationResult == ValidationResult.PendingUser)
                    {
                        issues.Add(
                            $"[INFO] Phase {phase.PhaseId} is waiting for user validation. " +
                            $"Validation {latest.ValidationId} is PendingUser. " +
                            "Use 'bfwf validation request-user' to re-prompt.");
                    }
                }
            }

            // Check 6: Validation required by contract but no validation log exists
            if (phase.Contract?.RequiresValidation == true)
            {
                var hasValidation = validations.Any(v =>
                    string.Equals(v.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase));

                var hasLegacyTest = tests.Any(t =>
                    string.Equals(t.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase));

                if (!hasValidation && !hasLegacyTest &&
                    phase.State is not PhaseState.Planned
                    and not PhaseState.ReadyForImplementation
                    and not PhaseState.AssignedToImplementation
                    and not PhaseState.InImplementation
                    and not PhaseState.ImplementationLogged
                    and not PhaseState.AuditLogged)
                {
                    issues.Add(
                        $"[WARNING] Phase {phase.PhaseId} requires validation " +
                        $"but has no validation records. Current state: {phase.State}");
                }
            }
        }

        return OperationResult.Ok(new { valid = issues.Count(i => i.StartsWith("[ERROR]")) == 0, issues });
    }
}
