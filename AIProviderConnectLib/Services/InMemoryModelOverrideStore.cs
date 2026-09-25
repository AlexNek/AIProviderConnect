using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

namespace AIProviderConnect.Services;

/// <summary>
/// Default in-memory <see cref="IModelOverrideStore"/> populated at registration time via
/// <see cref="Add"/>. Provider ids are matched case-insensitively.
/// </summary>
public sealed class InMemoryModelOverrideStore : IModelOverrideStore
{
    private readonly object _sync = new();

    private readonly Dictionary<string, List<ModelOverride>> _overrides =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds overrides for a provider id, appending to any already registered for that id.
    /// </summary>
    /// <param name="providerId">The provider id the overrides apply to.</param>
    /// <param name="overrides">The overrides to register, in merge order.</param>
    public void Add(string providerId, IEnumerable<ModelOverride> overrides)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        ArgumentNullException.ThrowIfNull(overrides);

        lock (_sync)
        {
            if (!_overrides.TryGetValue(providerId, out var list))
            {
                list = [];
                _overrides[providerId] = list;
            }

            list.AddRange(overrides);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelOverride> Get(string providerId)
    {
        lock (_sync)
        {
            return _overrides.TryGetValue(providerId, out var list)
                ? list.ToArray()
                : [];
        }
    }
}
