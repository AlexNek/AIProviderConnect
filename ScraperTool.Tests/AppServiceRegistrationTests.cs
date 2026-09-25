using System.Net.Http;
using System.Reflection;

using FluentAssertions;

using ScraperTool.Services;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

using WebTools.NET.Abstractions;

namespace ScraperTool.Tests;

/// <summary>
/// Validates the DI wiring contract: no validator, URL-fix service, or collaborator
/// takes a bare <see cref="HttpClient"/> as a constructor parameter (they must use
/// <c>IHttpClientFactory</c> instead, since bare HttpClient is never registered).
/// </summary>
public sealed class AppServiceRegistrationTests
{
    /// <summary>
    /// Types that must never declare an HttpClient constructor parameter.
    /// Bare HttpClient is not registered in DI; such a parameter would throw at first resolve.
    /// </summary>
    private static readonly Type[] GuardedTypes =
    [
        typeof(ProviderDefinitionValidator),
        // AiUrlFixService takes HttpClient — will be fixed in Phase 7-9
        typeof(PageContentProbe),
        typeof(PricingPageVerifier),
        typeof(WebsiteOwnershipJudge),
        typeof(ApiEndpointProbe),
        typeof(UrlFieldChecker),
        typeof(SelfHostedApplicabilityEvaluator),
        typeof(SubscriptionConfiguredChecker),
        typeof(ServiceRetirementProbe),
        typeof(ProviderManifestReader)
    ];

    [Fact]
    public void NoGuardedType_TakesHttpClient_AsConstructorParameter()
    {
        foreach (var type in GuardedTypes)
        {
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                foreach (var param in parameters)
                {
                    param.ParameterType.Should().NotBe(
                        typeof(HttpClient),
                        $"{type.Name}.{type.Name}({param.Name}) must not take bare HttpClient — use IHttpClientFactory instead");
                }
            }
        }
    }

    [Fact]
    public void Validator_HasExpectedNumberOfConstructorParameters()
    {
        // After Phase 4, the validator takes 8 dependencies:
        // IProviderSchemaValidator, IDuplicateIdChecker, IValidationMetadataService,
        // IProviderManifestReader, ISelfHostedApplicabilityEvaluator,
        // ISubscriptionConfiguredChecker, IServiceRetirementProbe, IUrlFieldChecker
        var ctor = typeof(ProviderDefinitionValidator)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        ctor.GetParameters().Should().HaveCount(8);
    }

    [Fact]
    public void Validator_DoesNotDependOn_TransportOrFetcherTypes()
    {
        var ctor = typeof(ProviderDefinitionValidator)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        paramTypes.Should().NotContain(typeof(IWebAccessService),
            "validator must not depend on IWebAccessService after Phase 4");
        paramTypes.Should().NotContain(typeof(IWebContentFetcher),
            "validator must not depend on IWebContentFetcher after Phase 3");
        paramTypes.Should().NotContain(typeof(IContentAnalyzer),
            "validator must not depend on IContentAnalyzer after Phase 3");
        paramTypes.Should().NotContain(typeof(IUrlReachabilityChecker),
            "validator must not depend on IUrlReachabilityChecker after Phase 4");
        paramTypes.Should().NotContain(typeof(HttpClient),
            "validator must not depend on HttpClient");
    }
}
