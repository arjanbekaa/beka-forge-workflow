using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;

partial class Program
{
    internal static void CmdPersonas(string? wfRoot, bool json, CliOutputMode mode)
    {
        var result = DispatchPersonaOperation(wfRoot, WorkflowOperations.ListPersonas);
        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Persona listing failed.", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result.Data);
            return;
        }

        var payload = ToJsonElement(result.Data);
        var personas = payload.GetProperty("personas");
        var warnings = payload.TryGetProperty("warnings", out var warningElement)
            ? warningElement.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList()
            : new List<string>();

        Console.WriteLine("Personas");
        Console.WriteLine("========");
        foreach (var persona in personas.EnumerateArray())
        {
            var id = GetString(persona, "personaId");
            var displayName = GetString(persona, "displayName");
            var primaryActor = GetString(persona, "primaryActor");
            var summary = GetString(persona, "summary");
            Console.WriteLine($"- {displayName} ({id}) [{primaryActor}]");
            Console.WriteLine($"  {summary}");
        }

        if (warnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Warnings:");
            foreach (var warning in warnings)
                Console.WriteLine($"- {warning}");
        }
    }

    internal static void CmdPersona(string? wfRoot, string? personaId, bool json, CliOutputMode mode)
    {
        if (string.IsNullOrWhiteSpace(personaId))
        {
            CliRenderer.Error("--persona is required.", mode);
            Environment.Exit(2);
        }

        var result = DispatchPersonaOperation(
            wfRoot,
            WorkflowOperations.GetPersona,
            new Dictionary<string, object?> { ["personaId"] = personaId });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Persona lookup failed.", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result.Data);
            return;
        }

        var payload = ToJsonElement(result.Data);
        var persona = payload.GetProperty("persona");
        var policies = payload.GetProperty("taskPolicies");

        Console.WriteLine($"{GetString(persona, "displayName")} ({GetString(persona, "personaId")})");
        Console.WriteLine($"Primary actor: {GetString(persona, "primaryActor")}");
        Console.WriteLine(GetString(persona, "summary"));

        if (persona.TryGetProperty("aliases", out var aliases) && aliases.GetArrayLength() > 0)
            Console.WriteLine($"Aliases: {string.Join(", ", aliases.EnumerateArray().Select(item => item.GetString()))}");

        Console.WriteLine();
        Console.WriteLine("Task policies:");
        foreach (var policy in policies.EnumerateArray())
        {
            Console.WriteLine($"- {GetString(policy, "taskType")}: {GetString(policy, "summary")}");
        }
    }

    internal static void CmdRecommendPersona(
        string? wfRoot,
        string? task,
        string? requestedOperation,
        string? requestedActor,
        int maxResults,
        bool json,
        CliOutputMode mode)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            CliRenderer.Error("--task is required.", mode);
            Environment.Exit(2);
        }

        var result = DispatchPersonaOperation(
            wfRoot,
            WorkflowOperations.RecommendPersona,
            new Dictionary<string, object?>
            {
                ["task"] = task,
                ["requestedOperation"] = requestedOperation,
                ["requestedActor"] = requestedActor,
                ["maxResults"] = maxResults
            });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Persona recommendation failed.", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result.Data);
            return;
        }

        var payload = ToJsonElement(result.Data);
        var recommendations = payload.GetProperty("recommendations");
        Console.WriteLine($"Task: {GetString(payload, "task")}");
        Console.WriteLine();

        if (recommendations.GetArrayLength() == 0)
        {
            Console.WriteLine("No persona recommendation available.");
        }
        else
        {
            Console.WriteLine("Recommendations:");
            foreach (var recommendation in recommendations.EnumerateArray())
            {
                var personaIdText = GetString(recommendation, "personaId");
                var displayName = GetString(recommendation, "displayName");
                var score = recommendation.TryGetProperty("score", out var scoreElement) ? scoreElement.GetDouble() : 0;
                var taskTypes = recommendation.TryGetProperty("matchedTaskTypes", out var taskTypesElement)
                    ? string.Join(", ", taskTypesElement.EnumerateArray().Select(item => item.GetString()))
                    : string.Empty;
                Console.WriteLine($"- {displayName} ({personaIdText}) score {score:F1}");
                if (!string.IsNullOrWhiteSpace(taskTypes))
                    Console.WriteLine($"  task types: {taskTypes}");
            }
        }

        if (payload.TryGetProperty("warnings", out var warnings) && warnings.GetArrayLength() > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Warnings:");
            foreach (var warning in warnings.EnumerateArray())
                Console.WriteLine($"- {warning.GetString()}");
        }
    }

    internal static void CmdValidatePersonaTask(
        string? wfRoot,
        string? personaId,
        string? task,
        string? requestedOperation,
        string? requestedActor,
        string? requestedPhaseId,
        bool hasEvidence,
        bool humanApproved,
        bool json,
        CliOutputMode mode)
    {
        if (string.IsNullOrWhiteSpace(personaId) || string.IsNullOrWhiteSpace(task))
        {
            CliRenderer.Error("--persona and --task are required.", mode);
            Environment.Exit(2);
        }

        var result = DispatchPersonaOperation(
            wfRoot,
            WorkflowOperations.ValidatePersonaTask,
            new Dictionary<string, object?>
            {
                ["personaId"] = personaId,
                ["task"] = task,
                ["requestedOperation"] = requestedOperation,
                ["requestedActor"] = requestedActor,
                ["requestedPhaseId"] = requestedPhaseId,
                ["hasEvidence"] = hasEvidence,
                ["humanApproved"] = humanApproved
            });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Persona-task validation failed.", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result.Data);
            if (!GetBool(ToJsonElement(result.Data), "isValid"))
                Environment.Exit(1);
            return;
        }

        var payload = ToJsonElement(result.Data);
        var isValid = GetBool(payload, "isValid");
        Console.WriteLine($"Persona: {GetString(payload, "displayName")} ({GetString(payload, "personaId")})");
        Console.WriteLine($"Result: {(isValid ? "VALID" : "INVALID")}");

        var matchedTaskType = GetString(payload, "matchedTaskType");
        if (!string.IsNullOrWhiteSpace(matchedTaskType))
            Console.WriteLine($"Task policy: {matchedTaskType}");

        if (payload.TryGetProperty("requiredContext", out var requiredContext) && requiredContext.GetArrayLength() > 0)
            Console.WriteLine($"Required context: {string.Join(", ", requiredContext.EnumerateArray().Select(item => item.GetString()))}");

        if (payload.TryGetProperty("issues", out var issues) && issues.GetArrayLength() > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Issues:");
            foreach (var issue in issues.EnumerateArray())
                Console.WriteLine($"- {issue.GetString()}");
        }

        if (payload.TryGetProperty("warnings", out var warnings) && warnings.GetArrayLength() > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Warnings:");
            foreach (var warning in warnings.EnumerateArray())
                Console.WriteLine($"- {warning.GetString()}");
        }

        if (!isValid)
            Environment.Exit(1);
    }

    private static OperationResult DispatchPersonaOperation(
        string? wfRoot,
        string operation,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
            return OperationResult.Fail("NotInitialized", "No Beka Forge Workflow project is initialized.");

        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);
        return dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            Actor = WorkflowActor.Implementer,
            Parameters = parameters ?? new Dictionary<string, object?>()
        });
    }
}
