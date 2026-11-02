using System.Text.Json;
using CodexControlDeck.Core;

namespace CodexControlDeck.Tests;

public sealed class DeckActionCoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "codex-control-deck-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RejectsRemoteActionWithoutExplicitConfirmationAndAcknowledgement()
    {
        var errors = DeckActionValidator.Validate(ProgramAction() with
        {
            AllowRemote = true,
            RequiresConfirmation = false,
            RemoteAccessAcknowledged = false
        });

        Assert.Contains(errors, error => error.Contains("подтверждения", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Telegram", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoteMetadataDoesNotExposeTargetArgumentsOrCommandText()
    {
        var action = ProgramAction() with
        {
            Target = @"C:\Secret Folder\private.exe",
            Arguments = ["--token", "secret-value"],
            AllowRemote = true,
            RequiresConfirmation = true,
            RemoteAccessAcknowledged = true
        };
        var metadata = new DeckActionMetadata(action.Id, action.Name, action.Description, action.Aliases, true, 0);

        var json = JsonSerializer.Serialize(metadata);

        Assert.DoesNotContain("private.exe", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-value", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreUsesAtomicReplacementAndRecoversFromLastGoodCopy()
    {
        var store = new DeckActionStore(_directory);
        var first = ProgramAction() with { Name = "Первый" };
        await store.SaveAsync([first]);
        await store.SaveAsync([first with { Name = "Второй" }]);
        await File.WriteAllTextAsync(store.FilePath, "{broken-json");

        var recovered = await store.LoadAsync();

        Assert.Equal("Первый", Assert.Single(recovered).Name);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void PlannerPreservesEveryArgumentAsASeparateValue()
    {
        var action = ProgramAction() with { Arguments = ["--name", "Value With Spaces", "& calc.exe"] };

        var plan = new DeckActionExecutionPlanner().Create(action);

        Assert.Equal(action.Arguments, plan.Arguments);
        Assert.False(plan.UseShellExecute);
    }

    [Fact]
    public async Task ServiceReturnsOnlyRemoteActionsAndRequiresConfirmation()
    {
        var store = new DeckActionStore(_directory);
        var remote = ProgramAction() with
        {
            Id = "remote-action",
            AllowRemote = true,
            RequiresConfirmation = true,
            RemoteAccessAcknowledged = true
        };
        await store.SaveAsync([ProgramAction(), remote]);
        var executor = new RecordingExecutor();
        var service = new DeckActionService(store, executor);

        Assert.Equal("remote-action", Assert.Single(await service.GetRemoteMetadataAsync()).Id);
        Assert.False((await service.ExecuteRemoteAsync("remote-action", confirmed: false)).Success);
        Assert.True((await service.ExecuteRemoteAsync("remote-action", confirmed: true)).Success);
        Assert.Equal("remote-action", executor.LastAction?.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static DeckActionDefinition ProgramAction() => new()
    {
        Id = "local-action",
        Name = "Сценарий",
        Description = "Проверка",
        Kind = DeckActionKind.Program,
        Target = @"C:\Windows\System32\notepad.exe",
        Aliases = ["открой блокнот"]
    };

    private sealed class RecordingExecutor : IDeckActionExecutor
    {
        public DeckActionDefinition? LastAction { get; private set; }

        public Task<DeckActionExecutionResult> ExecuteAsync(DeckActionDefinition action, CancellationToken cancellationToken = default)
        {
            LastAction = action;
            return Task.FromResult(new DeckActionExecutionResult(true, "ok"));
        }
    }
}
