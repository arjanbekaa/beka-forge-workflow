using System.Text.Json;
using System.Text.Json.Serialization;
using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;

string? rootArg = null;
int port = 0;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--root" && i + 1 < args.Length)
    {
        rootArg = args[++i];
    }
    else if (args[i] == "--port" && i + 1 < args.Length)
    {
        int.TryParse(args[++i], out port);
    }
}

var startDir = rootArg ?? Directory.GetCurrentDirectory();
var workflowRoot = rootArg is null
    ? DiscoverWorkflowRoot(startDir)
    : (WorkflowLayout.IsInitialized(startDir) ? Path.GetFullPath(startDir) : null);

Console.WriteLine(workflowRoot is not null
    ? $"Workflow root: {workflowRoot}"
    : $"No Beka Forge Workflow project found under '{startDir}'. Server will start in uninitialized mode.");

var effectiveRoot = workflowRoot ?? startDir;
var store = new WorkflowStore(effectiveRoot);
var dispatcher = new OperationDispatcher(store);

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
    Converters =
    {
        new WorkflowActorJsonConverter(),
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
    }
};

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://localhost:{port}");

var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    initialized = store.IsInitialized(),
    operations = dispatcher.RegisteredOperations.Count,
    workflowRoot = Path.GetFullPath(effectiveRoot)
}));

app.MapGet("/api/workflow/operations", () => Results.Ok(dispatcher.RegisteredOperations));

app.MapPost("/api/workflow/{operationName}", async (string operationName, HttpRequest request) =>
{
    if (string.IsNullOrWhiteSpace(operationName))
    {
        return Results.BadRequest(new { error = "Operation name must not be empty." });
    }

    JsonElement body;
    try
    {
        using var reader = new StreamReader(request.Body);
        var raw = await reader.ReadToEndAsync();
        body = string.IsNullOrWhiteSpace(raw)
            ? default
            : JsonSerializer.Deserialize<JsonElement>(raw, jsonOptions);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Invalid JSON body." });
    }

    string? phaseId = null;
    var actor = WorkflowActor.WorkflowKit;
    var parameters = new Dictionary<string, object?>();

    if (body.ValueKind == JsonValueKind.Object)
    {
        if (body.TryGetProperty("phaseId", out var phaseElement) &&
            phaseElement.ValueKind == JsonValueKind.String)
        {
            phaseId = phaseElement.GetString();
        }

        if (body.TryGetProperty("actor", out var actorElement) &&
            actorElement.ValueKind == JsonValueKind.String)
        {
            actor = ParseActor(actorElement.GetString());
        }

        if (body.TryGetProperty("parameters", out var parametersElement) &&
            parametersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in parametersElement.EnumerateObject())
            {
                parameters[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number when property.Value.TryGetInt64(out var longValue) => longValue,
                    JsonValueKind.Number => property.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
            }
        }
    }

    var result = dispatcher.Dispatch(new OperationContext
    {
        Operation = operationName,
        Actor = actor,
        PhaseId = phaseId,
        Parameters = parameters
    });

    return Results.Ok(result);
});

app.MapPost("/api/shutdown", async (IHostApplicationLifetime lifetime) =>
{
    await Task.Delay(100);
    lifetime.StopApplication();
    return Results.Ok(new { message = "Shutting down." });
});

app.MapGet("/api/shutdown", () =>
    Results.Ok(new { message = "Send POST /api/shutdown to stop the server." }));

Console.WriteLine($"Beka Forge Workflow server listening on {string.Join(", ", app.Urls)}");
app.Run();

static string? DiscoverWorkflowRoot(string startDir)
{
    var dir = Path.GetFullPath(startDir);

    while (true)
    {
        if (WorkflowLayout.IsInitialized(dir))
        {
            return dir;
        }

        var parent = Path.GetDirectoryName(dir);
        if (parent is null || parent == dir)
        {
            return null;
        }

        dir = parent;
    }
}

static WorkflowActor ParseActor(string? name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        return WorkflowActor.WorkflowKit;
    }

    return Enum.TryParse<WorkflowActor>(name, ignoreCase: true, out var actor)
        ? actor
        : WorkflowActor.WorkflowKit;
}
