namespace CodexControlDeck.Core;

public sealed record DeckActionExecutionPlan(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool UseShellExecute,
    string? WorkingDirectory = null);

public sealed class DeckActionExecutionPlanner(string powerShellExecutable = "pwsh.exe")
{
    public DeckActionExecutionPlan Create(DeckActionDefinition action)
    {
        DeckActionValidator.EnsureValid(action);
        return action.Kind switch
        {
            DeckActionKind.Program => new(action.Target, action.Arguments, false, Path.GetDirectoryName(action.Target)),
            DeckActionKind.Url => new(action.Target, Array.Empty<string>(), true),
            DeckActionKind.PowerShellScript => new(powerShellExecutable,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", action.Target, .. action.Arguments], false),
            DeckActionKind.PowerShellCommand => new(powerShellExecutable,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", action.Target], false),
            _ => throw new ArgumentOutOfRangeException(nameof(action.Kind))
        };
    }
}
