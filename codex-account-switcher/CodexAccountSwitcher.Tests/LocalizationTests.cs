using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("ru", AppLanguage.Russian)]
    [InlineData("en", AppLanguage.English)]
    [InlineData("zh", AppLanguage.Chinese)]
    [InlineData("unknown", AppLanguage.Russian)]
    public void FromStorageNormalizesLanguageCodes(string value, AppLanguage expected)
    {
        Assert.Equal(expected, Localizer.FromStorage(value));
    }

    [Fact]
    public void LocalizerProvidesThreeUiLanguages()
    {
        Assert.Equal("Аккаунты Codex", Localizer.Get(AppLanguage.Russian, "app.title"));
        Assert.Equal("Codex Accounts", Localizer.Get(AppLanguage.English, "app.title"));
        Assert.Equal("Codex 账号", Localizer.Get(AppLanguage.Chinese, "app.title"));
    }

    [Fact]
    public void LocalizerFallsBackToRussianForUnknownKeys()
    {
        Assert.Equal("missing.key", Localizer.Get(AppLanguage.English, "missing.key"));
    }
}
