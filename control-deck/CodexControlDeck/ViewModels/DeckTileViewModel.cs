namespace CodexControlDeck.ViewModels;

public sealed record DeckTileViewModel(
    string Id,
    string Name,
    string Description,
    string Glyph,
    bool RequiresConfirmation = false,
    bool IsCustom = false);

public sealed record AccountViewModel(string Name, string DisplayName, bool Active);

public sealed record UsageViewModel(string Account, string Window, string Percent, string Reset);

public sealed record ActionListItemViewModel(string Id, string Name, string Kind, bool AllowRemote);
