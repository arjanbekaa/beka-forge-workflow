using System.Reflection;
using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server;

internal sealed class PersonaPolicyService(WorkflowStore store)
{
    private static readonly HashSet<string> CanonicalOperations = typeof(WorkflowOperations)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
        .Select(field => (string)field.GetValue(null)!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] GlobalUnsafePhrases =
    [
        "raw write",
        "write workflow.json",
        "edit .workflowkit",
        "modify jsonl",
        "rewrite log",
        "skip validation",
        "bypass validation",
        "mark passed without evidence",
        "self approve",
        "approve own work",
        "skip release gate",
        "bypass release gate"
    ];

    private readonly PersonaCatalogStore _catalogStore = new(store.WorkflowRoot);

    public PersonaCatalogLoadResult LoadCatalog() => _catalogStore.Load();

    public PersonaProfile? ResolvePersona(PersonaCatalog catalog, string personaIdOrAlias)
    {
        return catalog.Personas.FirstOrDefault(persona =>
            string.Equals(persona.PersonaId, personaIdOrAlias, StringComparison.OrdinalIgnoreCase)
            || persona.Aliases.Any(alias => string.Equals(alias, personaIdOrAlias, StringComparison.OrdinalIgnoreCase)));
    }

    public PersonaRecommendationResult Recommend(
        string task,
        string? requestedOperation,
        string? requestedActor,
        int maxResults)
    {
        var loadResult = LoadCatalog();
        var recommendations = new List<PersonaRecommendationEntry>();

        foreach (var persona in loadResult.Catalog.Personas)
        {
            var candidate = ScorePersona(loadResult.Catalog, persona, task, requestedOperation, requestedActor);
            if (candidate is not null)
                recommendations.Add(candidate);
        }

        var ordered = recommendations
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.PersonaId, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(maxResults, 1))
            .ToList();

        var warnings = new List<string>(loadResult.Warnings);
        if (ordered.Count == 0)
            warnings.Add("No persona matched the task strongly enough. Manual selection is required.");

        return new PersonaRecommendationResult
        {
            Task = task,
            RequestedOperation = requestedOperation,
            RequestedActor = requestedActor,
            CatalogSource = loadResult.Source,
            Warnings = warnings,
            Recommendations = ordered
        };
    }

    public PersonaTaskValidationResult Validate(
        string personaIdOrAlias,
        string task,
        string? requestedOperation,
        string? requestedActor,
        string? requestedPhaseId,
        bool hasEvidence,
        bool humanApproved)
    {
        var loadResult = LoadCatalog();
        var persona = ResolvePersona(loadResult.Catalog, personaIdOrAlias);
        if (persona is null)
        {
            return new PersonaTaskValidationResult
            {
                IsValid = false,
                PersonaId = personaIdOrAlias,
                DisplayName = personaIdOrAlias,
                CatalogSource = loadResult.Source,
                RequestedOperation = requestedOperation,
                RequestedActor = requestedActor,
                RequestedPhaseId = requestedPhaseId,
                Issues = [$"Persona '{personaIdOrAlias}' was not found in the catalog."],
                Warnings = loadResult.Warnings
            };
        }

        var issues = new List<string>();
        var warnings = new List<string>(loadResult.Warnings);
        var matchingPolicies = GetPoliciesForPersona(loadResult.Catalog, persona)
            .Select(policy => new { Policy = policy, Score = ScorePolicy(persona, policy, task, requestedOperation, requestedActor) })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Policy.TaskType, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var match = matchingPolicies.FirstOrDefault(item => item.Score > 0)
            ?? matchingPolicies.FirstOrDefault();
        if (match is null)
        {
            issues.Add($"Persona '{persona.PersonaId}' does not have a task policy that matches the requested task.");
            return new PersonaTaskValidationResult
            {
                IsValid = false,
                PersonaId = persona.PersonaId,
                DisplayName = persona.DisplayName,
                CatalogSource = loadResult.Source,
                RequestedOperation = requestedOperation,
                RequestedActor = requestedActor,
                RequestedPhaseId = requestedPhaseId,
                Issues = issues,
                Warnings = warnings
            };
        }

        var policy = match.Policy;
        var lowerTask = task.ToLowerInvariant();

        if (match.Score <= 0)
            warnings.Add($"Task text did not strongly match persona '{persona.PersonaId}' policy keywords; strict operation checks were applied.");

        foreach (var phrase in GlobalUnsafePhrases)
        {
            if (lowerTask.Contains(phrase, StringComparison.Ordinal))
                issues.Add($"Task contains forbidden phrase '{phrase}'. Personas cannot authorize that request.");
        }

        foreach (var phrase in policy.DisallowedPhrases)
        {
            if (lowerTask.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                issues.Add($"Task violates the '{policy.TaskType}' policy phrase rule: '{phrase}'.");
        }

        foreach (var keyword in persona.ProhibitedTaskKeywords)
        {
            if (lowerTask.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                issues.Add($"Task conflicts with persona '{persona.PersonaId}' prohibited keyword '{keyword}'.");
        }

        if (policy.RequiresPhaseContext && string.IsNullOrWhiteSpace(requestedPhaseId))
            issues.Add($"Task policy '{policy.TaskType}' requires a phase ID.");

        if (!string.IsNullOrWhiteSpace(requestedActor)
            && !policy.AllowedActors.Any(actor => string.Equals(actor.ToString(), requestedActor, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(
                $"Actor '{requestedActor}' is not allowed for persona '{persona.PersonaId}' task policy '{policy.TaskType}'.");
        }

        if (!string.IsNullOrWhiteSpace(requestedOperation))
        {
            if (!CanonicalOperations.Contains(requestedOperation))
            {
                issues.Add($"Operation '{requestedOperation}' is not a canonical WorkflowKit operation.");
            }
            else if (policy.ForbiddenOperations.Contains(requestedOperation, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add($"Task policy '{policy.TaskType}' forbids operation '{requestedOperation}'.");
            }
            else if (!policy.AllowedOperations.Contains(requestedOperation, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add($"Task policy '{policy.TaskType}' does not allow operation '{requestedOperation}'.");
            }
        }

        if (policy.ForbidSelfApproval
            && (lowerTask.Contains("self approve", StringComparison.Ordinal)
                || lowerTask.Contains("approve own work", StringComparison.Ordinal)
                || string.Equals(requestedActor, WorkflowActor.Implementer.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("Self-approval is forbidden for this persona policy.");
        }

        if (policy.RequiresEvidence
            && (lowerTask.Contains("pass", StringComparison.Ordinal)
                || lowerTask.Contains("passed", StringComparison.Ordinal)
                || string.Equals(requestedOperation, WorkflowOperations.CreateValidationLog, StringComparison.OrdinalIgnoreCase))
            && !hasEvidence)
        {
            issues.Add("This task needs evidence before it can be treated as a passing validation.");
        }

        if (policy.RequiresHumanApproval && !humanApproved)
            issues.Add("This task requires explicit HumanOwner approval.");

        return new PersonaTaskValidationResult
        {
            IsValid = issues.Count == 0,
            PersonaId = persona.PersonaId,
            DisplayName = persona.DisplayName,
            CatalogSource = loadResult.Source,
            RequestedOperation = requestedOperation,
            RequestedActor = requestedActor,
            RequestedPhaseId = requestedPhaseId,
            MatchedTaskType = policy.TaskType,
            RequiredContext = policy.RequiredContext,
            Issues = issues,
            Warnings = warnings
        };
    }

    private static PersonaRecommendationEntry? ScorePersona(
        PersonaCatalog catalog,
        PersonaProfile persona,
        string task,
        string? requestedOperation,
        string? requestedActor)
    {
        var reasons = new List<string>();
        var matchedTaskTypes = new List<string>();
        double totalScore = 0;

        foreach (var policy in GetPoliciesForPersona(catalog, persona))
        {
            var policyScore = ScorePolicy(persona, policy, task, requestedOperation, requestedActor);
            if (policyScore <= 0)
                continue;

            matchedTaskTypes.Add(policy.TaskType);
            totalScore += policyScore;
            reasons.Add($"Matched task policy '{policy.TaskType}'.");
        }

        if (!string.IsNullOrWhiteSpace(requestedActor)
            && string.Equals(requestedActor, persona.PrimaryActor.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            totalScore += 1.5;
            reasons.Add($"Actor matches persona primary actor '{persona.PrimaryActor}'.");
        }

        if (totalScore <= 0)
            return null;

        return new PersonaRecommendationEntry
        {
            PersonaId = persona.PersonaId,
            DisplayName = persona.DisplayName,
            PrimaryActor = persona.PrimaryActor,
            Score = totalScore,
            MatchedTaskTypes = matchedTaskTypes,
            Reasons = reasons
        };
    }

    private static double ScorePolicy(
        PersonaProfile persona,
        TaskExecutionPolicy policy,
        string task,
        string? requestedOperation,
        string? requestedActor)
    {
        var lowerTask = task.ToLowerInvariant();
        double score = 0;

        if (persona.PreferredTaskKeywords.Any(keyword =>
            lowerTask.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            score += 1;
        }

        foreach (var keyword in policy.Keywords)
        {
            if (lowerTask.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                score += 1.5;
        }

        if (!string.IsNullOrWhiteSpace(requestedOperation)
            && policy.AllowedOperations.Contains(requestedOperation, StringComparer.OrdinalIgnoreCase))
        {
            score += 3;
        }

        if (!string.IsNullOrWhiteSpace(requestedActor)
            && policy.AllowedActors.Any(actor => string.Equals(actor.ToString(), requestedActor, StringComparison.OrdinalIgnoreCase)))
        {
            score += 1;
        }

        if (policy.DisallowedPhrases.Any(phrase => lowerTask.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            || persona.ProhibitedTaskKeywords.Any(keyword => lowerTask.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            score -= 4;
        }

        return score;
    }

    private static IReadOnlyList<TaskExecutionPolicy> GetPoliciesForPersona(PersonaCatalog catalog, PersonaProfile persona)
    {
        var taskTypes = persona.SupportedTaskTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return catalog.TaskPolicies
            .Where(policy => taskTypes.Contains(policy.TaskType))
            .ToList();
    }
}
