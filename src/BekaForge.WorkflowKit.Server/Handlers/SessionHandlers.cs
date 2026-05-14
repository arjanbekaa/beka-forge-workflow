using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Git;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>Lists recent sessions with optional status filter.</summary>
public sealed class ListSessionsHandler(GitStore gitStore) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ListSessions;

    public OperationResult Execute(OperationContext context)
    {
        var maxResults = context.Get<int?>("maxResults") ?? 20;
        var activeOnly = context.GetBool("activeOnly");

        var sessions = gitStore.ListSessions(maxResults: Math.Min(maxResults, 50));

        if (activeOnly)
            sessions = sessions.Where(s => s.IsActive).ToList();

        return OperationResult.Ok(new { sessions, count = sessions.Count });
    }
}

/// <summary>Returns the current active session, if any.</summary>
public sealed class GetCurrentSessionHandler(GitStore gitStore, SessionIdentity identity) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetCurrentSession;

    public OperationResult Execute(OperationContext context)
    {
        var session = gitStore.GetCurrentSession();

        return OperationResult.Ok(new
        {
            hasActiveSession = session is not null,
            session,
            identity = new
            {
                identity.UserName,
                identity.UserEmail,
                identity.MachineName,
                identity.Platform,
                identity.GitConfigAvailable
            }
        });
    }
}

/// <summary>Ends the current active session.</summary>
public sealed class EndSessionHandler(GitStore gitStore) : IOperationHandler
{
    public string OperationName => WorkflowOperations.EndSession;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            var current = gitStore.GetCurrentSession();
            if (current is null)
                return OperationResult.Fail("NoActiveSession", "No active session to end.");

            sessionId = current.SessionId;
        }

        gitStore.EndSession(sessionId);

        return OperationResult.Ok(new
        {
            ended = true,
            sessionId,
            timestamp = DateTimeOffset.UtcNow
        });
    }
}
