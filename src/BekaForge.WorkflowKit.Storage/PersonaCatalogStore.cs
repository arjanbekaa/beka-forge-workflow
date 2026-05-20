using System.Reflection;
using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Loads the workflow persona catalog from persisted workflow-owned files.
/// Falls back to a safe built-in catalog when files are missing or invalid.
/// </summary>
public sealed class PersonaCatalogStore(string workflowRoot)
{
    private static readonly HashSet<string> SafeOperationAllowlist =
    [
        WorkflowOperations.GetState,
        WorkflowOperations.GetCurrentPhase,
        WorkflowOperations.ListPhases,
        WorkflowOperations.GetPhaseContract,
        WorkflowOperations.GetContextBundle,
        WorkflowOperations.GetRelevantContext,
        WorkflowOperations.GetValidationPlan,
        WorkflowOperations.RequestUserValidation,
        WorkflowOperations.SearchOperations,
        WorkflowOperations.RecommendOperation,
        WorkflowOperations.ExplainOperation,
        WorkflowOperations.StartPhase,
        WorkflowOperations.CompleteImplementation,
        WorkflowOperations.CreateImplementationLog,
        WorkflowOperations.CreateAuditLog,
        WorkflowOperations.CreateReviewLog,
        WorkflowOperations.CreateValidationLog,
        WorkflowOperations.CreateFixLog
    ];

    private static readonly HashSet<string> CanonicalOperations = typeof(WorkflowOperations)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
        .Select(field => (string)field.GetValue(null)!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public PersonaCatalogLoadResult Load()
    {
        var warnings = new List<string>();
        var personasPath = WorkflowLayout.PersonaProfilesPath(workflowRoot);
        var taskPoliciesPath = WorkflowLayout.TaskPoliciesPath(workflowRoot);

        if (!File.Exists(personasPath) || !File.Exists(taskPoliciesPath))
        {
            if (!File.Exists(personasPath))
                warnings.Add($"Persona profiles file not found: {personasPath}");
            if (!File.Exists(taskPoliciesPath))
                warnings.Add($"Task policy file not found: {taskPoliciesPath}");

            return new PersonaCatalogLoadResult
            {
                Catalog = CreateDefaultCatalog(),
                Source = "builtInDefault",
                Warnings = warnings
            };
        }

        try
        {
            var personas = WorkflowSerializer.Deserialize<List<PersonaProfile>>(File.ReadAllText(personasPath));
            var taskPolicies = WorkflowSerializer.Deserialize<List<TaskExecutionPolicy>>(File.ReadAllText(taskPoliciesPath));

            if (personas is null || taskPolicies is null)
            {
                warnings.Add("Persona catalog files could not be deserialized. Falling back to safe defaults.");
                return new PersonaCatalogLoadResult
                {
                    Catalog = CreateDefaultCatalog(),
                    Source = "builtInDefault",
                    Warnings = warnings
                };
            }

            var validationIssues = ValidateCatalog(personas, taskPolicies);
            if (validationIssues.Count > 0)
            {
                warnings.AddRange(validationIssues);
                warnings.Add("Persisted persona catalog failed validation. Falling back to safe defaults.");
                return new PersonaCatalogLoadResult
                {
                    Catalog = CreateDefaultCatalog(),
                    Source = "builtInDefault",
                    Warnings = warnings
                };
            }

            return new PersonaCatalogLoadResult
            {
                Catalog = new PersonaCatalog
                {
                    Personas = personas,
                    TaskPolicies = taskPolicies
                },
                Source = "persisted",
                Warnings = warnings
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            warnings.Add($"Persona catalog load failed: {ex.Message}");
            warnings.Add("Falling back to safe built-in persona defaults.");
            return new PersonaCatalogLoadResult
            {
                Catalog = CreateDefaultCatalog(),
                Source = "builtInDefault",
                Warnings = warnings
            };
        }
    }

    public static void InitializeDefaultsIfMissing(string workflowRoot)
    {
        var personasPath = WorkflowLayout.PersonaProfilesPath(workflowRoot);
        var taskPoliciesPath = WorkflowLayout.TaskPoliciesPath(workflowRoot);

        Directory.CreateDirectory(WorkflowLayout.PersonasDir(workflowRoot));

        var catalog = CreateDefaultCatalog();
        if (!File.Exists(personasPath))
            File.WriteAllText(personasPath, WorkflowSerializer.SerializeState(catalog.Personas));
        if (!File.Exists(taskPoliciesPath))
            File.WriteAllText(taskPoliciesPath, WorkflowSerializer.SerializeState(catalog.TaskPolicies));
    }

    private static List<string> ValidateCatalog(
        IReadOnlyList<PersonaProfile> personas,
        IReadOnlyList<TaskExecutionPolicy> taskPolicies)
    {
        var issues = new List<string>();
        var taskTypes = taskPolicies
            .Select(policy => policy.TaskType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var personaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var persona in personas)
        {
            if (string.IsNullOrWhiteSpace(persona.PersonaId))
            {
                issues.Add("Persona profile is missing personaId.");
                continue;
            }

            if (!personaIds.Add(persona.PersonaId))
                issues.Add($"Duplicate persona ID '{persona.PersonaId}'.");

            foreach (var taskType in persona.SupportedTaskTypes)
            {
                if (!taskTypes.Contains(taskType))
                    issues.Add($"Persona '{persona.PersonaId}' references unknown task type '{taskType}'.");
            }
        }

        var taskTypeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in taskPolicies)
        {
            if (string.IsNullOrWhiteSpace(policy.TaskType))
            {
                issues.Add("Task policy is missing taskType.");
                continue;
            }

            if (!taskTypeIds.Add(policy.TaskType))
                issues.Add($"Duplicate task policy '{policy.TaskType}'.");

            foreach (var operation in policy.AllowedOperations)
            {
                if (!CanonicalOperations.Contains(operation))
                {
                    issues.Add($"Task policy '{policy.TaskType}' references unknown operation '{operation}'.");
                    continue;
                }

                if (!SafeOperationAllowlist.Contains(operation))
                    issues.Add($"Task policy '{policy.TaskType}' cannot authorize unsafe operation '{operation}'.");
            }

            foreach (var operation in policy.ForbiddenOperations)
            {
                if (!CanonicalOperations.Contains(operation))
                    issues.Add($"Task policy '{policy.TaskType}' references unknown forbidden operation '{operation}'.");
            }

            if (policy.AllowedOperations.Intersect(policy.ForbiddenOperations, StringComparer.OrdinalIgnoreCase).Any())
                issues.Add($"Task policy '{policy.TaskType}' contains the same operation in allowed and forbidden lists.");
        }

        return issues;
    }

    private static PersonaCatalog CreateDefaultCatalog()
    {
        var personas = new List<PersonaProfile>
        {
            new()
            {
                PersonaId = "planner",
                DisplayName = "Planner",
                Summary = "Shapes phase scope, contracts, and next-step guidance without implementing code.",
                PrimaryActor = WorkflowActor.Planner,
                Aliases = ["codex", "architect"],
                SupportedTaskTypes = ["planning"],
                PreferredTaskKeywords = ["plan", "scope", "contract", "phase", "decompose", "lane"],
                ProhibitedTaskKeywords = ["implement", "approve own work", "skip validation"]
            },
            new()
            {
                PersonaId = "implementer",
                DisplayName = "Implementer",
                Summary = "Implements scoped code changes and records implementation evidence.",
                PrimaryActor = WorkflowActor.Implementer,
                Aliases = ["deepseek", "claude", "builder"],
                SupportedTaskTypes = ["implementation", "fix"],
                PreferredTaskKeywords = ["implement", "build", "handler", "cli", "model", "storage", "test"],
                ProhibitedTaskKeywords = ["approve", "review", "skip validation"]
            },
            new()
            {
                PersonaId = "auditor",
                DisplayName = "Auditor",
                Summary = "Performs audit checks and records concrete findings without mutating product code.",
                PrimaryActor = WorkflowActor.Auditor,
                Aliases = ["audit", "self-audit"],
                SupportedTaskTypes = ["audit"],
                PreferredTaskKeywords = ["audit", "findings", "risks", "inspect"],
                ProhibitedTaskKeywords = ["implement", "approve", "rewrite"]
            },
            new()
            {
                PersonaId = "reviewer",
                DisplayName = "Reviewer",
                Summary = "Makes independent review decisions and does not implement code through the persona path.",
                PrimaryActor = WorkflowActor.Reviewer,
                Aliases = ["review", "gate"],
                SupportedTaskTypes = ["review"],
                PreferredTaskKeywords = ["review", "approve", "gate", "decision"],
                ProhibitedTaskKeywords = ["self approve", "implement", "bypass"]
            },
            new()
            {
                PersonaId = "validator",
                DisplayName = "Validator",
                Summary = "Validates behavior with evidence and escalates to humans when required.",
                PrimaryActor = WorkflowActor.Validator,
                Aliases = ["tester", "unityassistant", "validation"],
                SupportedTaskTypes = ["validation"],
                PreferredTaskKeywords = ["validate", "test", "evidence", "run", "verify"],
                ProhibitedTaskKeywords = ["skip validation", "mark passed without evidence", "bypass release gate"]
            }
        };

        var taskPolicies = new List<TaskExecutionPolicy>
        {
            new()
            {
                TaskType = "planning",
                Summary = "Phase and contract planning work.",
                Keywords = ["plan", "scope", "contract", "phase", "dependency", "lane", "next action"],
                AllowedActors = [WorkflowActor.Planner],
                AllowedOperations =
                [
                    WorkflowOperations.GetState,
                    WorkflowOperations.GetCurrentPhase,
                    WorkflowOperations.ListPhases,
                    WorkflowOperations.GetPhaseContract,
                    WorkflowOperations.GetContextBundle,
                    WorkflowOperations.GetRelevantContext,
                    WorkflowOperations.SearchOperations,
                    WorkflowOperations.RecommendOperation,
                    WorkflowOperations.ExplainOperation
                ],
                ForbiddenOperations =
                [
                    WorkflowOperations.CreateImplementationLog,
                    WorkflowOperations.CreateReviewLog,
                    WorkflowOperations.CreateValidationLog
                ],
                RequiredContext = ["phaseId", "phase contract", "dependency state"],
                DisallowedPhrases = ["approve own work", "skip validation", "bypass release gate"]
            },
            new()
            {
                TaskType = "implementation",
                Summary = "Product code implementation within scoped workflow areas.",
                Keywords = ["implement", "build", "add", "change code", "handler", "cli", "storage", "model"],
                AllowedActors = [WorkflowActor.Implementer],
                AllowedOperations =
                [
                    WorkflowOperations.GetState,
                    WorkflowOperations.GetPhaseContract,
                    WorkflowOperations.GetContextBundle,
                    WorkflowOperations.GetRelevantContext,
                    WorkflowOperations.SearchOperations,
                    WorkflowOperations.RecommendOperation,
                    WorkflowOperations.ExplainOperation,
                    WorkflowOperations.StartPhase,
                    WorkflowOperations.CompleteImplementation,
                    WorkflowOperations.CreateImplementationLog
                ],
                ForbiddenOperations =
                [
                    WorkflowOperations.CreateReviewLog,
                    WorkflowOperations.CreateValidationLog,
                    WorkflowOperations.RequestUserValidation
                ],
                RequiredContext = ["phaseId", "phase contract", "code context"],
                DisallowedPhrases = ["self approve", "skip validation", "raw write", "bypass release gate"]
            },
            new()
            {
                TaskType = "fix",
                Summary = "Targeted follow-up fix work after review or blocker findings.",
                Keywords = ["fix", "address review", "follow-up", "repair", "resolve issue"],
                AllowedActors = [WorkflowActor.Implementer, WorkflowActor.Fixer],
                AllowedOperations =
                [
                    WorkflowOperations.GetPhaseContract,
                    WorkflowOperations.GetContextBundle,
                    WorkflowOperations.GetRelevantContext,
                    WorkflowOperations.SearchOperations,
                    WorkflowOperations.RecommendOperation,
                    WorkflowOperations.ExplainOperation,
                    WorkflowOperations.CreateFixLog,
                    WorkflowOperations.CreateImplementationLog
                ],
                ForbiddenOperations =
                [
                    WorkflowOperations.CreateReviewLog,
                    WorkflowOperations.CreateValidationLog
                ],
                RequiredContext = ["phaseId", "review findings or blocker context", "code context"],
                DisallowedPhrases = ["approve own work", "skip validation"]
            },
            new()
            {
                TaskType = "audit",
                Summary = "Audit inspection with concrete findings and recommendations.",
                Keywords = ["audit", "inspect", "findings", "risks", "recommendations"],
                AllowedActors = [WorkflowActor.Auditor],
                AllowedOperations =
                [
                    WorkflowOperations.GetPhaseContract,
                    WorkflowOperations.GetContextBundle,
                    WorkflowOperations.GetRelevantContext,
                    WorkflowOperations.SearchOperations,
                    WorkflowOperations.RecommendOperation,
                    WorkflowOperations.ExplainOperation,
                    WorkflowOperations.CreateAuditLog
                ],
                ForbiddenOperations =
                [
                    WorkflowOperations.CreateImplementationLog,
                    WorkflowOperations.CreateReviewLog,
                    WorkflowOperations.CreateValidationLog
                ],
                RequiredContext = ["phaseId", "implemented changes", "acceptance criteria"],
                DisallowedPhrases = ["implement code", "approve own work", "rewrite history"]
            },
            new()
            {
                TaskType = "review",
                Summary = "Independent review and gate decision work.",
                Keywords = ["review", "approve", "gate", "decision", "requires fix"],
                AllowedActors = [WorkflowActor.Reviewer],
                AllowedOperations =
                [
                    WorkflowOperations.GetPhaseContract,
                    WorkflowOperations.GetContextBundle,
                    WorkflowOperations.GetRelevantContext,
                    WorkflowOperations.SearchOperations,
                    WorkflowOperations.RecommendOperation,
                    WorkflowOperations.ExplainOperation,
                    WorkflowOperations.CreateReviewLog
                ],
                ForbiddenOperations =
                [
                    WorkflowOperations.CreateImplementationLog,
                    WorkflowOperations.CreateAuditLog,
                    WorkflowOperations.CreateValidationLog
                ],
                RequiredContext = ["phaseId", "audit findings", "implemented changes"],
                DisallowedPhrases = ["approve own work", "self approve", "implement code"],
                ForbidSelfApproval = true
            },
            new()
            {
                TaskType = "validation",
                Summary = "Evidence-backed validation work with honest escalation.",
                Keywords = ["validate", "test", "verify", "evidence", "run command", "manual validation"],
                AllowedActors = [WorkflowActor.Validator],
                AllowedOperations =
                [
                    WorkflowOperations.GetValidationPlan,
                    WorkflowOperations.GetPhaseContract,
                    WorkflowOperations.GetContextBundle,
                    WorkflowOperations.GetRelevantContext,
                    WorkflowOperations.SearchOperations,
                    WorkflowOperations.RecommendOperation,
                    WorkflowOperations.ExplainOperation,
                    WorkflowOperations.CreateValidationLog,
                    WorkflowOperations.RequestUserValidation
                ],
                ForbiddenOperations =
                [
                    WorkflowOperations.SkipValidation,
                    WorkflowOperations.CompleteUserValidation,
                    WorkflowOperations.CreateReviewLog
                ],
                RequiredContext = ["phaseId", "validation plan", "test command or inspection target"],
                DisallowedPhrases =
                [
                    "skip validation",
                    "bypass validation",
                    "mark passed without evidence",
                    "bypass release gate"
                ],
                RequiresEvidence = true
            }
        };

        return new PersonaCatalog
        {
            Personas = personas,
            TaskPolicies = taskPolicies
        };
    }
}
