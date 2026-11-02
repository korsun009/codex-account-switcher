using System.Text.Json;

namespace CodexControlDeck.Services;

public sealed class DeckSettingsStore
{
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexControlDeck");
    private string FilePath => Path.Combine(_directory, "settings.json");

    public async Task<DeckSettings> LoadAsync()
    {
        if (!File.Exists(FilePath))
        {
            return new DeckSettings();
        }

        try
        {
            await using var stream = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync<DeckSettings>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new DeckSettings();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new DeckSettings();
        }
    }

    public async Task SaveAsync(DeckSettings settings)
    {
        Directory.CreateDirectory(_directory);
        var temporary = FilePath + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        }
        File.Move(temporary, FilePath, overwrite: true);
    }
}

public sealed record DeckSettings(string? ApiBaseUrl = null);
