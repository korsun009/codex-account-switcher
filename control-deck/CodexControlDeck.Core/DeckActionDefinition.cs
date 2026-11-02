namespace CodexControlDeck.Core;

public enum DeckActionKind
{
    Program,
    Url,
    PowerShellScript,
    PowerShellCommand
}

public sealed record DeckActionDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public DeckActionKind Kind { get; init; }
    public string Target { get; init; } = "";
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    public bool AllowRemote { get; init; }
    public bool RequiresConfirmation { get; init; }
    public bool RemoteAccessAcknowledged { get; init; }
    public int Order { get; init; }
}

public sealed record DeckActionMetadata(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Aliases,
    bool RequiresConfirmation,
    int Order);

public sealed record DeckActionExecutionResult(bool Success, string Message, int? ProcessId = null);
