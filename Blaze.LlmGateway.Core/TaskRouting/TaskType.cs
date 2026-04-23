namespace Blaze.LlmGateway.Core.TaskRouting;

/// <summary>
/// Represents the classified task type of an incoming chat request.
/// Used by <see cref="ITaskClassifier"/> to drive provider selection in codebrewRouter.
/// </summary>
public enum TaskType
{
    /// <summary>Mathematical proofs, logical deduction, multi-step analysis.</summary>
    Reasoning,

    /// <summary>Code generation, debugging, refactoring, unit tests.</summary>
    Coding,

    /// <summary>Deep research, literature surveys, comprehensive topic analysis.</summary>
    Research,

    /// <summary>
    /// Image-based tasks: object detection, scene description, visual QA.
    /// NOTE: current API only supports text content; routing is keyword-based until
    /// ChatMessageDto gains image-part support.
    /// </summary>
    VisionObjectDetection,

    /// <summary>Creative writing: stories, poems, essays, fiction, blog posts.</summary>
    Creative,

    /// <summary>Data analysis, statistics, CSV/SQL queries, charting.</summary>
    DataAnalysis,

    /// <summary>Default when no specific task type can be determined.</summary>
    General
}
