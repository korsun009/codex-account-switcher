namespace CodexControlDeck.Core;

public sealed class DeckActionService(DeckActionStore store, IDeckActionExecutor? executor = null)
{
    private readonly IDeckActionExecutor _executor = executor ?? new DeckActionExecutor();

    public async Task<IReadOnlyList<DeckActionMetadata>> GetRemoteMetadataAsync(CancellationToken cancellationToken = default)
    {
        var actions = await store.LoadAsync(cancellationToken);
        return actions
            .Where(action => action.AllowRemote)
            .OrderBy(action => action.Order)
            .Select(action => new DeckActionMetadata(
                action.Id,
                action.Name,
                action.Description,
                action.Aliases.Select(ActionAliasNormalizer.Normalize).Where(alias => alias.Length > 0).Distinct().ToArray(),
                action.RequiresConfirmation,
                action.Order))
            .ToArray();
    }

    public async Task<DeckActionExecutionResult> ExecuteRemoteAsync(string id, bool confirmed, CancellationToken cancellationToken = default)
    {
        var actions = await store.LoadAsync(cancellationToken);
        var action = actions.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        if (action is null || !action.AllowRemote)
        {
            return new DeckActionExecutionResult(false, "Сценарий не найден или не разрешён для удалённого запуска.");
        }

        if (action.RequiresConfirmation && !confirmed)
        {
            return new DeckActionExecutionResult(false, "Требуется подтверждение запуска.");
        }

        return await _executor.ExecuteAsync(action, cancellationToken);
    }
}
