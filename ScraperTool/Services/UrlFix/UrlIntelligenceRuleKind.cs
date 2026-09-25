namespace ScraperTool.Services.UrlFix;

public enum UrlIntelligenceRuleKind
{
    RootLinkScanner = 0,

    SearchWeb = 1,

    RobotsTxtScanner = 2,

    SitemapParser = 3,

    WaybackFallback = 4,

    RegionalEndpointFallback = 5,

    FetchProviderDocs = 6,

    BrokenUrlResearch = 7,

    BaseUrlValidation = 8,

    ModelDiscoveryResearch = 9,

    FindWebsiteUrl = 10,

    FindLoginUrl = 11,

    FindDocumentationUrl = 12,

    AuthGatedDocsVerification = 13,

    DecisionTreeResearch = 14
}
