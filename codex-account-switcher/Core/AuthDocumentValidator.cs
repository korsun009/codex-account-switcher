using System.Text.Json;

namespace CodexAccountSwitcher.Core;

public static class AuthDocumentValidator
{
    public const int MaximumAuthDocumentLength = 4 * 1024 * 1024;

    public static void Validate(ReadOnlySpan<byte> authDocument)
    {
        if (authDocument.IsEmpty || authDocument.Length > MaximumAuthDocumentLength)
        {
            throw new InvalidDataException("auth.json пуст или имеет недопустимый размер.");
        }

        try
        {
            using var document = JsonDocument.Parse(authDocument.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("auth.json должен содержать JSON-объект.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("auth.json поврежден или имеет неожиданный формат.", ex);
        }
    }
}
