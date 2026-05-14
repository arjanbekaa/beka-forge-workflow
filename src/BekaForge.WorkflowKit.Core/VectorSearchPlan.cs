namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// PHASE-024-F: Vector Search Planning — Deferred Plan/Prototype
///
/// Vector search (semantic embeddings) for context retrieval is deferred to a future phase.
/// This class serves as the planning artifact documenting the strategy.
///
/// ## Rationale
/// 1. Deterministic hybrid retrieval already covers exact, path, metadata, TF-IDF, fuzzy, and C# symbols.
/// 2. Any vector index would be a rebuildable read model — never source of truth.
/// 3. Dependency cost: ONNX model ~80MB or external embedding service.
/// 4. Domain fit: structured metadata dominates; TF-IDF + fuzzy covers free-text needs.
///
/// ## Prototype Architecture (if activated)
/// - .workflowkit/index/vectors/ with embeddings.bin + manifest.json
/// - Option A: SQLite BLOB + ONNX (portable, ~80MB model)
/// - Option B: External embedding API (better embeddings, adds latency/cost)
///
/// ## Integration Points
/// 1. ContextIndexBuilder: optional vector table during rebuild
/// 2. HybridContextRetriever: vector_similarity signal added
/// 3. RebuildContextIndexHandler: --with-vectors flag
/// 4. BudgetProfile: IncludeVectorSearch boolean
///
/// ## Scoring: 60% base + 30% hybrid + 10% vector
///
/// ## Deferral Criteria
/// Activate when: TF-IDF+fuzzy insufficient, lightweight self-contained model found,
/// user feedback confirms semantic value.
/// </summary>
public static class VectorSearchPlan
{
    /// <summary>Whether vector search is currently enabled (always false in this phase).</summary>
    public static bool IsEnabled => false;

    /// <summary>Planned model name for future activation.</summary>
    public const string PlannedModel = "all-MiniLM-L6-v2";

    /// <summary>Planned embedding dimensions.</summary>
    public const int PlannedDimensions = 384;

    /// <summary>Status summary for reporting.</summary>
    public static string Status => "Deferred — plan/prototype only. See PHASE-024 contract.";
}
