using AIProviderConnect.Abstractions;
using AIProviderConnect.Constants;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIProviderConnect.DependencyInjection;

/// <summary>
/// Extension methods for registering AI providers from the provider catalog (JSON definitions).
/// </summary>
public static class AIProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers all AI providers defined in the embedded JSON catalog.
    /// Each provider is registered with named options and a singleton IAIProvider handler
    /// based on its wire protocol type. Option defaults are seeded from each definition.
    /// </summary>
    public static IServiceCollection AddAiProviders(this IServiceCollection services)
    {
        return RegisterCore(services, new ProviderCatalog(), null, null, null, null, null);
    }

    /// <summary>
    /// Merges consumer-supplied definitions over the embedded catalog (replace by id, else append),
    /// then registers every provider in the resulting catalog.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="customProviders">Consumer definitions to merge over the embedded catalog.</param>
    public static IServiceCollection AddAiProviders(
        this IServiceCollection services,
        IEnumerable<ProviderDefinition> customProviders)
    {
        ArgumentNullException.ThrowIfNull(customProviders);

        var definitions =
            customProviders as IReadOnlyList<ProviderDefinition> ?? customProviders.ToList();
        CustomProviderDefinitionValidator.Validate(definitions);

        return RegisterCore(services, new ProviderCatalog(definitions), null, null, null, null, null);
    }

    /// <summary>
    /// Full builder registration: add or replace definitions, configure named options, and
    /// register consumer-supplied <see cref="IAIProvider"/> implementations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A callback that populates the registration builder.</param>
    public static IServiceCollection AddAiProviders(
        this IServiceCollection services,
        Action<AIProviderRegistrationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new AIProviderRegistrationBuilder();
        configure(builder);

        var catalog = new ProviderCatalog();
        var definitions = builder.BuildDefinitions(catalog);
        CustomProviderDefinitionValidator.Validate(definitions);
        catalog.Merge(definitions);

        return RegisterCore(
            services,
            catalog,
            builder.OptionRegistrations,
            builder.CustomProviders,
            builder.ManuallyRegisteredIds,
            builder.ModelOverrideStore,
            builder.CredentialResolver);
    }

    private static IServiceCollection RegisterCore(
        IServiceCollection services,
        ProviderCatalog catalog,
        IReadOnlyList<Action<IServiceCollection>>? optionRegistrations,
        IReadOnlyDictionary<string, Func<IServiceProvider, IAIProvider>>? customProviders,
        IReadOnlySet<string>? manuallyRegisteredIds,
        IModelOverrideStore? modelOverrideStore,
        ICredentialResolver? credentialResolver)
    {
        services.AddSingleton(catalog);
        services.AddSingleton<IProviderCatalog>(catalog);
        services.AddSingleton<IAIProviderFactory, DefaultAIProviderFactory>();

        if (credentialResolver is not null)
        {
            services.AddSingleton(credentialResolver);
        }

        if (modelOverrideStore is not null)
        {
            services.AddSingleton(modelOverrideStore);
        }

        if (customProviders is not null)
        {
            foreach (var (providerId, factory) in customProviders)
            {
                RegisterInstance(services, providerId, factory);
            }
        }

        foreach (var provider in catalog.All)
        {
            if (manuallyRegisteredIds is not null && manuallyRegisteredIds.Contains(provider.Id))
                continue;

            RegisterProvider(services, catalog, provider);
        }

        // Consumer named-options callbacks are applied after seeding so their values win.
        if (optionRegistrations is not null)
        {
            foreach (var registration in optionRegistrations)
            {
                registration(services);
            }
        }

        return services;
    }

    private static void SeedFromDefinition(AIProviderOptions options, ProviderDefinition definition)
    {
        if (!string.IsNullOrEmpty(definition.BaseUrl))
            options.BaseUrl = definition.BaseUrl;

        // Independent `is` checks, not a switch: OpenAICompatibleProviderOptions matches
        // IChatAndModelsEndpointOptions first, so an appended interface arm would be unreachable.
        if (options is IChatAndModelsEndpointOptions chatModels)
        {
            if (!string.IsNullOrEmpty(definition.ChatEndpoint))
                chatModels.ChatEndpoint = definition.ChatEndpoint;
            if (!string.IsNullOrEmpty(definition.ModelsEndpoint))
                chatModels.ModelsEndpoint = definition.ModelsEndpoint;

            var chat = EndpointOperations.Find(definition.Endpoints, EndpointOperations.Chat);
            if (!string.IsNullOrEmpty(chat?.Path))
                chatModels.ChatEndpoint = chat.Path;

            var models = EndpointOperations.Find(definition.Endpoints, EndpointOperations.Models);
            if (!string.IsNullOrEmpty(models?.Path))
                chatModels.ModelsEndpoint = models.Path;
        }

        if (options is MessagesApiOptions messages)
        {
            if (!string.IsNullOrEmpty(definition.MessagesEndpoint))
                messages.MessagesEndpoint = definition.MessagesEndpoint;
            if (!string.IsNullOrEmpty(definition.ModelsEndpoint))
                messages.ModelsEndpoint = definition.ModelsEndpoint;

            // The `messages` operation entry maps to MessagesEndpoint for a MessagesApi primary;
            // its ChatEndpoint (if any) is left untouched.
            var messagesEntry = EndpointOperations.Find(definition.Endpoints, EndpointOperations.Messages);
            if (!string.IsNullOrEmpty(messagesEntry?.Path))
                messages.MessagesEndpoint = messagesEntry.Path;

            var messagesModels = EndpointOperations.Find(definition.Endpoints, EndpointOperations.Models);
            if (!string.IsNullOrEmpty(messagesModels?.Path))
                messages.ModelsEndpoint = messagesModels.Path;
        }

        if (options is KeyQueryOptions keyQuery)
        {
            if (!string.IsNullOrEmpty(definition.ModelsEndpoint))
                keyQuery.ModelsEndpoint = definition.ModelsEndpoint;

            var keyQueryModels = EndpointOperations.Find(definition.Endpoints, EndpointOperations.Models);
            if (!string.IsNullOrEmpty(keyQueryModels?.Path))
                keyQuery.ModelsEndpoint = keyQueryModels.Path;
        }

        if (options is IEmbeddingsEndpointOptions embeddings)
        {
            var embeddingsEntry = EndpointOperations.Find(definition.Endpoints, EndpointOperations.Embeddings);
            if (!string.IsNullOrEmpty(embeddingsEntry?.Path))
                embeddings.EmbeddingsEndpoint = embeddingsEntry.Path;
            if (!string.IsNullOrEmpty(embeddingsEntry?.BaseUrl))
                embeddings.EmbeddingsBaseUrl = embeddingsEntry.BaseUrl;
        }

        if (options is IDecisionsEndpointOptions decisions)
        {
            var decisionsEntry = EndpointOperations.Find(definition.Endpoints, EndpointOperations.Decisions);
            if (!string.IsNullOrEmpty(decisionsEntry?.Path))
                decisions.DecisionsEndpoint = decisionsEntry.Path;
            if (!string.IsNullOrEmpty(decisionsEntry?.BaseUrl))
                decisions.DecisionsBaseUrl = decisionsEntry.BaseUrl;
        }

        // Copy protocol-specific configuration generically — no key knowledge here.
        options.ProtocolConfiguration = definition.ProtocolConfiguration;
    }

    private static void Register<TOptions, TProvider>(
        IServiceCollection services,
        IProviderCatalog catalog,
        string providerId,
        Func<HttpClient, TOptions, IProviderCatalog, string, ILogger, ICredentialResolver?, TProvider> factory)
        where TOptions : AIProviderOptions
        where TProvider : class, IAIProvider
    {
        services.AddOptions<TOptions>(providerId)
            .Configure(o => SeedFromDefinition(o, catalog.Get(providerId)!));

        RegisterInstance(services, providerId, sp => factory(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<IOptionsFactory<TOptions>>().Create(providerId),
            sp.GetRequiredService<IProviderCatalog>(),
            providerId,
            ResolveLogger<TOptions>(sp),
            sp.GetService<ICredentialResolver>()));
    }

    /// <summary>
    /// Resolves the logger category for <typeparamref name="TOptions"/>. The library depends only on
    /// Logging.Abstractions, so a host that never called <c>AddLogging()</c> has no <see cref="ILoggerFactory"/>
    /// registered; such a host gets the shared null logger instead of a failed provider construction.
    /// </summary>
    private static ILogger ResolveLogger<TOptions>(IServiceProvider services) =>
        services.GetService<ILoggerFactory>() is { } factory
            ? factory.CreateLogger<TOptions>()
            : NullLogger.Instance;

    private static void RegisterInstance(
        IServiceCollection services, string providerId, Func<IServiceProvider, IAIProvider> factory)
    {
        services.AddKeyedSingleton<IAIProvider>(providerId,
            (sp, _) => ApplyModelOverrides(sp, providerId, factory(sp)));
        services.AddKeyedSingleton<ProviderActivator>(providerId,
            (sp, _) => new ProviderActivator(sp, factory, providerId));
    }

    private static void RegisterProvider(
        IServiceCollection services,
        ProviderCatalog providerCatalog,
        ProviderDefinition provider)
    {
        var providerId = provider.Id;
        var protocol = provider.Protocol;

        // Runs for every catalog entry, embedded and consumer-supplied alike, before any
        // class selection reads the endpoints block.
        EndpointConfiguration.Validate(providerId, provider);

        switch (protocol)
        {
            case EProviderProtocol.Native:
                throw new InvalidOperationException(
                    $"Provider '{providerId}' declares the Native protocol, which has no built-in " +
                    "wire implementation. Register a consumer-supplied provider for this id via " +
                    "AddAiProviders(b => b.AddProvider<TProvider>(...)) instead of relying on " +
                    "automatic registration.");

            case EProviderProtocol.OpenAICompatible:
                if (EndpointConfiguration.TryGetDecisionsOverride(provider, out _))
                {
                    RegisterDecisionCombination<OpenAICompatibleProviderOptions>(services, providerCatalog, providerId);
                    break;
                }

                Register<OpenAICompatibleProviderOptions, OpenAICompatibleProvider>(services, providerCatalog, providerId,
                                    (client, options, catalog, pid, logger, resolver) => new OpenAICompatibleProvider(client, options, catalog, pid, logger, resolver));
                break;

            case EProviderProtocol.MessagesApi:
                Register<MessagesApiOptions, MessagesApiProvider>(services, providerCatalog, providerId,
                                    (client, options, catalog, pid, logger, resolver) => new MessagesApiProvider(client, options, catalog, pid, logger, resolver));
                services.Configure<MessagesApiOptions>(providerId, o =>
                    MessagesApiProtocol.ApplyProtocolConfiguration(o));
                break;

            case EProviderProtocol.HybridGateway:
                if (EndpointConfiguration.TryGetDecisionsOverride(provider, out _))
                {
                    RegisterDecisionCombination<HybridGatewayProviderOptions>(services, providerCatalog, providerId);
                    break;
                }

                Register<HybridGatewayProviderOptions, OpenAICompatibleProvider>(services, providerCatalog, providerId,
                                    (client, options, catalog, pid, logger, resolver) => new OpenAICompatibleProvider(client, options, catalog, pid, logger, resolver));
                break;

            case EProviderProtocol.KeyQuery:
                Register<KeyQueryOptions, KeyQueryProvider>(services, providerCatalog, providerId,
                                    (client, options, catalog, pid, logger, resolver) => new KeyQueryProvider(client, options, catalog, pid, logger, resolver));
                services.Configure<KeyQueryOptions>(providerId, o =>
                    KeyQueryWireProtocol.ApplyProtocolConfiguration(o));
                break;

            case EProviderProtocol.Catalog:
                Register<OpenAICompatibleProviderOptions, ModelCatalogProvider>(services, providerCatalog, providerId,
                                    (client, options, catalog, pid, logger, resolver) => new ModelCatalogProvider(client, options, catalog, pid, logger, resolver));
                services.Configure<OpenAICompatibleProviderOptions>(providerId, o =>
                    CatalogWireProtocol.ApplyProtocolConfiguration(o));
                break;

            case EProviderProtocol.Decision:
                Register<DecisionProviderOptions, DecisionProvider>(services, providerCatalog, providerId,
                                    (client, options, catalog, pid, logger, resolver) => new DecisionProvider(client, options, catalog, pid, logger, resolver));
                services.Configure<DecisionProviderOptions>(providerId, o =>
                    DecisionsWireProtocol.ApplyProtocolConfiguration(o));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported provider protocol.");
        }
    }

    /// <summary>
    /// Registers the one class serving an OpenAI-compatible/hybrid-gateway primary and its
    /// decisions override under a single id (rule 12), keyed on the definition — not on a
    /// provider-id convention. The options type is the branch's own, so consumer
    /// <c>Configure&lt;TOptions&gt;(providerId, ...)</c> callbacks target the instance the
    /// provider actually reads.
    /// </summary>
    /// <typeparam name="TOptions">The protocol-specific options type for the primary surface.</typeparam>
    private static void RegisterDecisionCombination<TOptions>(
        IServiceCollection services,
        ProviderCatalog providerCatalog,
        string providerId)
        where TOptions : AIProviderOptions, IDecisionsEndpointOptions
    {
        Register<TOptions, OpenAICompatibleDecisionProvider>(services, providerCatalog, providerId,
                            (client, options, catalog, pid, logger, resolver) => new OpenAICompatibleDecisionProvider(client, options, catalog, pid, logger, resolver));
        services.Configure<TOptions>(providerId, o =>
            DecisionsWireProtocol.ApplyProtocolConfiguration(o));
    }

    /// <summary>
    /// Wraps <paramref name="inner"/> in a model-override decorator only when a model-override
    /// store is registered and holds entries for <paramref name="providerId"/>.
    /// Providers without overrides are returned unchanged. When the inner provider supports
    /// streaming, the streaming-capable decorator is used; otherwise a non-streaming decorator
    /// is selected so the <c>is IStreamingChatProvider</c> check remains accurate.
    /// </summary>
    internal static IAIProvider ApplyModelOverrides(
        IServiceProvider serviceProvider,
        string providerId,
        IAIProvider inner)
    {
        var store = serviceProvider.GetService<IModelOverrideStore>();
        if (store is null || store.Get(providerId).Count == 0)
            return inner;

        if (inner is IStreamingChatProvider && inner is IDecisionProvider)
            return new StreamingDecisionModelCatalogOverrideDecorator(inner, store);

        if (inner is IStreamingChatProvider)
            return new ModelCatalogOverrideDecorator(inner, store);

        if (inner is IDecisionProvider)
            return new DecisionModelCatalogOverrideDecorator(inner, store);

        return new NonStreamingModelCatalogOverrideDecorator(inner, store);
    }
}
