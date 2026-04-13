using System;

namespace SchemaStudio.AIHelpers;

public enum AICommandStatus
{
    Pending,
    Completed,
    Verified,
    Rejected
}

/// <summary>
/// Indicates that this member or module must not be structurally modified without explicit approval.
/// </summary>
[FileVersion("1.0")]
[AIInstructions("2026-04-10 10:31 AM CDT added partial-safe AIChangeAttribute to combine edit version, instruction text, and status in one repeatable marker.", AICommandStatus.Pending)]
[AIChange("1.1", "2026-04-10 12:10 PM CDT added AIFileContextAttribute for durable file-purpose and nuance headers that remain code-shaped for AI and human readers.", AICommandStatus.Pending)]
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
public sealed class DoNotRefactorAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;

    public string Warning => "CRITICAL: Do not refactor without asking. DO NOT remove or relocate existing comments. [cite: 2025-12-21]";
}

/// <summary>
/// Provides explicit instructions to AI tooling with status tracking.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AIInstructionsAttribute(string command, AICommandStatus status = AICommandStatus.Pending) : Attribute
{
    public string Command { get; } = command;

    public AICommandStatus Status { get; set; } = status;
}

/// <summary>
/// Combines an AI edit version marker with the instruction/status entry for partial-safe tracking.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AIChangeAttribute(string version, string command, AICommandStatus status = AICommandStatus.Pending) : Attribute
{
    public string Version { get; } = version;

    public string Command { get; } = command;

    public AICommandStatus Status { get; set; } = status;
}

/// <summary>
/// Describes a file's durable purpose, responsibilities, and local nuances for AI and human readers.
/// 2026-04-10 12:10 PM CDT AI context marker: AIFileContextAttribute is intended for code-shaped file headers that should survive cleanup.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AIFileContextAttribute(string fileName, string purpose) : Attribute
{
    public string FileName { get; } = fileName;

    public string Purpose { get; } = purpose;

    public string Responsibilities { get; set; } = string.Empty;

    public string Nuances { get; set; } = string.Empty;

    public string RelatedFiles { get; set; } = string.Empty;

    public string LastReviewed { get; set; } = string.Empty;
}

/// <summary>
/// Records an audit trail of changes made by AI.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AIHistoryAttribute(string version, string changeLog) : Attribute
{
    public string Version { get; } = version;

    public string ChangeLog { get; } = changeLog;

    public string Timestamp { get; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class UserHistoryAttribute(string version, string changeLog) : Attribute
{
    public string Version { get; } = version;

    public string ChangeLog { get; } = changeLog;

    public string Timestamp { get; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>
/// Tracks the version of the file as updated by AI tools.
/// 2026-04-10 10:31 AM CDT AI partial-safe marker: AIChangeAttribute should be used for new partial-class edit tracking because it allows multiple versioned instructions on the same type.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class)]
public sealed class FileVersionAttribute(string version) : Attribute
{
    public string Version { get; } = version;
}
