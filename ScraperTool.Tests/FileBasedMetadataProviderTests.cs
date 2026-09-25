using FluentAssertions;

using ScraperTool.Services.UrlResearch;

namespace ScraperTool.Tests;

public class FileBasedMetadataProviderTests
{
    private const string KeywordsJson = """
        {
          "subscriptionKeywords": [],
          "payAsYouGoKeywords": [],
          "pricingUrlKeywords": [],
          "apiBaseUrlMarkers": []
        }
        """;

    private const string FieldProfilesJson = """
        [
          {
            "fieldName": "loginUrl",
            "purpose": "Where a user signs in. Prose the loader must tolerate.",
            "searchQueryTemplate": "{providerName} login sign up",
            "relevanceTerms": ["signin", "sign-in", "sign in", "auth"],
            "siblingFields": ["apiPricingUrl", "documentationUrl"],
            "siblingPageMayBeAnswer": false
          },
          {
            "fieldName": "documentationUrl",
            "notApplicableValue": "-",
            "siblingFields": ["apiPricingUrl"]
          }
        ]
        """;

    private static (FileBasedMetadataProvider Provider, DirectoryInfo Dir) CreateProvider(
        string? keywordsJson = null,
        string? fieldProfilesJson = null)
    {
        var dir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        if (keywordsJson is not null)
            File.WriteAllText(Path.Combine(dir.FullName, "decision-keywords.json"), keywordsJson);
        if (fieldProfilesJson is not null)
            File.WriteAllText(Path.Combine(dir.FullName, "field-definitions.json"), fieldProfilesJson);
        return (new FileBasedMetadataProvider(dir.FullName), dir);
    }

    private static void Cleanup(DirectoryInfo dir) => dir.Delete(recursive: true);

    [Fact]
    public async Task GetDecisionKeywordsAsync_MissingConfigFile_ThrowsFileNotFound()
    {
        // Arrange — the JSON file is the single source of truth, so a missing
        // file must fail loudly instead of degrading to empty keyword sets.
        var (provider, dir) = CreateProvider();
        try
        {
            // Act
            var act = () => provider.GetDecisionKeywordsAsync();

            // Assert
            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task GetDecisionKeywordsAsync_LoadsKeywordsFromConfigFile()
    {
        // Arrange
        var json = """
            {
              "subscriptionKeywords": ["subscription"],
              "payAsYouGoKeywords": ["per token"],
              "pricingUrlKeywords": ["pricing"],
              "apiBaseUrlMarkers": ["base_url"]
            }
            """;
        var (provider, dir) = CreateProvider(json);
        try
        {
            // Act
            var keywords = await provider.GetDecisionKeywordsAsync();

            // Assert
            keywords.SubscriptionKeywords.Should().Equal("subscription");
            keywords.PayAsYouGoKeywords.Should().Equal("per token");
            keywords.PricingUrlKeywords.Should().Equal("pricing");
            keywords.ApiBaseUrlMarkers.Should().Equal("base_url");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task ValidateRequiredConfigAsync_Succeeds_WhenConfigFileExists()
    {
        // Arrange — startup fail-fast must pass with a valid config in place.
        var (provider, dir) = CreateProvider(KeywordsJson, FieldProfilesJson);
        try
        {
            // Act
            var act = () => provider.ValidateRequiredConfigAsync();

            // Assert
            await act.Should().NotThrowAsync();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task ValidateRequiredConfigAsync_MissingFieldDefinitions_ThrowsFileNotFound()
    {
        // The field profiles now drive research routing, so a missing file is as fatal at
        // startup as a missing keyword set — it must not degrade into "no field is configured".
        var (provider, dir) = CreateProvider(KeywordsJson);
        try
        {
            var act = () => provider.ValidateRequiredConfigAsync();

            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task GetProfileAsync_ReadsRoutingFromConfigFile()
    {
        // Arrange
        var (provider, dir) = CreateProvider(KeywordsJson, FieldProfilesJson);
        try
        {
            // Act
            var profile = await provider.GetProfileAsync("loginurl");

            // Assert — field kinds are matched case-insensitively, unknown members such as the
            // file's prose descriptions are ignored, and the declared order is kept.
            profile.Should().NotBeNull();
            profile!.RelevanceTerms.Should().Equal("signin", "sign-in", "sign in", "auth");
            profile.SiblingFields.Should().Equal("apiPricingUrl", "documentationUrl");
            profile.SiblingPageMayBeAnswer.Should().BeFalse();
            profile.SearchQueryTemplate.Should().Be("{providerName} login sign up");
            profile.NotApplicableValue.Should().BeNull();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task GetProfileAsync_OmittedAnswerFlag_KeepsSiblingPageEligible()
    {
        var (provider, dir) = CreateProvider(KeywordsJson, FieldProfilesJson);
        try
        {
            var profile = await provider.GetProfileAsync("documentationUrl");

            profile.Should().NotBeNull();
            profile!.SiblingPageMayBeAnswer.Should().BeTrue();
            profile.RelevanceTerms.Should().BeEmpty();
            profile.SearchQueryTemplate.Should().BeNull();
            profile.NotApplicableValue.Should().Be("-");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task GetProfileAsync_UnknownOrBlankFieldKind_ReturnsNull()
    {
        // A field kind without a profile is researched by the tree alone; the store must not
        // invent routing for it.
        var (provider, dir) = CreateProvider(KeywordsJson, FieldProfilesJson);
        try
        {
            (await provider.GetProfileAsync("modelDescription")).Should().BeNull();
            (await provider.GetProfileAsync("")).Should().BeNull();
            (await provider.GetProfileAsync("   ")).Should().BeNull();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task GetProfileAsync_ShippedConfig_StatesTheRoutingThePipelineDependsOn()
    {
        // Guards the shipped data file itself: the loader silently ignores a misspelled member,
        // so the routing the decision trees rely on has to be asserted here.
        var configDirectory = Path.Combine(AppContext.BaseDirectory, "Config");
        if (!File.Exists(Path.Combine(configDirectory, "field-definitions.json")))
        {
            configDirectory = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "ScraperTool", "Config"));
        }

        var provider = new FileBasedMetadataProvider(configDirectory);

        var loginProfile = await provider.GetProfileAsync("loginUrl");
        loginProfile.Should().NotBeNull();
        loginProfile!.SiblingFields.Should().NotBeEmpty();
        loginProfile.RelevanceTerms.Should().Contain("auth");
        loginProfile.SiblingPageMayBeAnswer.Should().BeFalse();

        var baseProfile = await provider.GetProfileAsync("baseUrl");
        baseProfile.Should().NotBeNull();
        baseProfile!.SiblingFields.Should().NotBeEmpty();

        var documentationProfile = await provider.GetProfileAsync("documentationUrl");
        documentationProfile.Should().NotBeNull();
        documentationProfile!.SiblingPageMayBeAnswer.Should().BeTrue();

        // Every researched field needs its own web-search wording and must not silently claim
        // a "not applicable" answer that its provider definition cannot hold.
        foreach (var fieldKind in new[]
                 {
                     "website", "loginUrl", "apiPricingUrl", "subscriptionPricingUrl",
                     "documentationUrl", "baseUrl", "minModelCount", "modelDescription"
                 })
        {
            var profile = await provider.GetProfileAsync(fieldKind);
            profile.Should().NotBeNull($"the {fieldKind} tree routes on its profile");
            profile!.SearchQueryTemplate.Should().NotBeNullOrWhiteSpace();
            string? expectedNotApplicable = fieldKind == "subscriptionPricingUrl" ? "-" : null;
            profile.NotApplicableValue.Should().Be(expectedNotApplicable);
        }
    }
}
