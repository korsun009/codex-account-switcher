using System.Diagnostics;

namespace CodexControlDeck.Core;

public interface IDeckActionExecutor
{
    Task<DeckActionExecutionResult> ExecuteAsync(DeckActionDefinition action, CancellationToken cancellationToken = default);
}

public sealed class DeckActionExecutor(DeckActionExecutionPlanner? planner = null) : IDeckActionExecutor
{
    private readonly DeckActionExecutionPlanner _planner = planner ?? new DeckActionExecutionPlanner();

    public Task<DeckActionExecutionResult> ExecuteAsync(DeckActionDefinition action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = _planner.Create(action);
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.FileName,
            UseShellExecute = plan.UseShellExecute,
            WorkingDirectory = plan.WorkingDirectory ?? "",
            CreateNoWindow = !plan.UseShellExecute
        };
        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows не смог запустить сценарий.");
        return Task.FromResult(new DeckActionExecutionResult(true, $"Сценарий «{action.Name}» запущен.", process.Id));
    }
}
