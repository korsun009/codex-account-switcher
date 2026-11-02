using System.Text.RegularExpressions;

namespace CodexControlDeck.Core;

public static partial class DeckActionValidator
{
    public static IReadOnlyList<string> Validate(DeckActionDefinition action)
    {
        var errors = new List<string>();
        if (!IdPattern().IsMatch(action.Id ?? ""))
        {
            errors.Add("ID должен начинаться с латинской буквы и содержать 3-48 строчных букв, цифр или дефисов.");
        }

        if (string.IsNullOrWhiteSpace(action.Name) || action.Name.Trim().Length > 64)
        {
            errors.Add("Название обязательно и должно быть короче 65 символов.");
        }

        if (action.Description?.Length > 180)
        {
            errors.Add("Описание должно быть короче 181 символа.");
        }

        if (action.Aliases.Any(alias => string.IsNullOrWhiteSpace(alias) || alias.Trim().Length > 64))
        {
            errors.Add("Каждая голосовая фраза должна содержать от 1 до 64 символов.");
        }

        if (action.Aliases.Select(ActionAliasNormalizer.Normalize).Where(alias => alias.Length > 0).Distinct().Count()
            != action.Aliases.Count(alias => !string.IsNullOrWhiteSpace(alias)))
        {
            errors.Add("Голосовые фразы не должны повторяться.");
        }

        ValidateTarget(action, errors);

        if (action.AllowRemote && !action.RequiresConfirmation)
        {
            errors.Add("Удалённый запуск пользовательского сценария требует подтверждения.");
        }

        if (action.AllowRemote && !action.RemoteAccessAcknowledged)
        {
            errors.Add("Подтвердите, что сценарий разрешено запускать через Telegram и Алису.");
        }

        return errors;
    }

    public static void EnsureValid(DeckActionDefinition action)
    {
        var errors = Validate(action);
        if (errors.Count > 0)
        {
            throw new DeckActionValidationException(errors);
        }
    }

    private static void ValidateTarget(DeckActionDefinition action, ICollection<string> errors)
    {
        var target = action.Target?.Trim() ?? "";
        switch (action.Kind)
        {
            case DeckActionKind.Program when !Path.IsPathFullyQualified(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase):
                errors.Add("Для программы выберите полный путь к EXE-файлу.");
                break;
            case DeckActionKind.Url when !Uri.TryCreate(target, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp):
                errors.Add("Укажите полный адрес сайта с https:// или http://.");
                break;
            case DeckActionKind.PowerShellScript when !Path.IsPathFullyQualified(target) || !target.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase):
                errors.Add("Для PowerShell-сценария выберите полный путь к PS1-файлу.");
                break;
            case DeckActionKind.PowerShellCommand when string.IsNullOrWhiteSpace(target) || target.Length > 8000:
                errors.Add("Команда PowerShell должна содержать от 1 до 8000 символов.");
                break;
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{2,47}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}

public sealed class DeckActionValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
