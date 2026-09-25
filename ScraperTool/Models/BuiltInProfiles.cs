using AiCleverness.Models;

namespace ScraperTool.Models;

public static class BuiltInProfiles
{
    public const string FallbackId = "fallback";

    public const string PrimaryId = "primary";

    public static IReadOnlyList<CapabilityProfile> GetAll() =>
        [
            new()
                {
                    Id = PrimaryId,
                    Name = "Primary model",
                    Capabilities = new()
                                       {
                                           CapabilityFlags =
                                               EModelCapability.TextGeneration
                                               | EModelCapability.StructuredOutput,
                                           CostTier = ECostTier.Cheap,
                                           MinContextWindow = 128_000
                                       },
                    Priority = 1
                },
            new()
                {
                    Id = FallbackId,
                    Name = "Fallback model",
                    Capabilities = new()
                                       {
                                           CapabilityFlags =
                                               EModelCapability.TextGeneration
                                               | EModelCapability.StructuredOutput,
                                           CostTier = ECostTier.Cheap,
                                           MinContextWindow = 128_000
                                       },
                    Priority = 2
                }
        ];
}
