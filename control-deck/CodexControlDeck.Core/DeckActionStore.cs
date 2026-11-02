using System.Text.Json;

namespace CodexControlDeck.Core;

public sealed class DeckActionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public DeckActionStore(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodexControlDeck");
        FilePath = Path.Combine(DirectoryPath, "actions.json");
        BackupPath = Path.Combine(DirectoryPath, "actions.last-good.json");
    }

    public string DirectoryPath { get; }
    public string FilePath { get; }
    public string BackupPath { get; }

    public async Task<IReadOnlyList<DeckActionDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return Array.Empty<DeckActionDefinition>();
        }

        try
        {
            return await ReadAsync(FilePath, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or DeckActionValidationException)
        {
            if (!File.Exists(BackupPath))
            {
                throw new InvalidDataException("Файл сценариев повреждён, резервной копии нет.", error);
            }

            return await ReadAsync(BackupPath, cancellationToken);
        }
    }

    public async Task SaveAsync(IEnumerable<DeckActionDefinition> actions, CancellationToken cancellationToken = default)
    {
        var snapshot = actions.OrderBy(action => action.Order).ThenBy(action => action.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        foreach (var action in snapshot)
        {
            DeckActionValidator.EnsureValid(action);
        }

        if (snapshot.Select(action => action.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
        {
            throw new DeckActionValidationException(["ID сценариев не должны повторяться."]);
        }

        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = Path.Combine(DirectoryPath, $"actions.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, new StoreEnvelope(1, DateTimeOffset.UtcNow, snapshot), JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(FilePath))
            {
                File.Replace(temporaryPath, FilePath, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, FilePath);
                File.Copy(FilePath, BackupPath, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<IReadOnlyList<DeckActionDefinition>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var envelope = await JsonSerializer.DeserializeAsync<StoreEnvelope>(stream, JsonOptions, cancellationToken)
            ?? throw new JsonException("Файл сценариев пуст.");
        if (envelope.SchemaVersion != 1)
        {
            throw new JsonException($"Неподдерживаемая версия файла сценариев: {envelope.SchemaVersion}.");
        }

        foreach (var action in envelope.Actions)
        {
            DeckActionValidator.EnsureValid(action);
        }

        return envelope.Actions.OrderBy(action => action.Order).ToArray();
    }

    private sealed record StoreEnvelope(int SchemaVersion, DateTimeOffset UpdatedAt, IReadOnlyList<DeckActionDefinition> Actions);
}
