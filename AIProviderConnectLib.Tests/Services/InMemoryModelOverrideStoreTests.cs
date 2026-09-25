using AIProviderConnect.Models;
using AIProviderConnect.Services;

using FluentAssertions;

namespace AIProviderConnect.Tests.Services;

public class InMemoryModelOverrideStoreTests
{
    [Fact]
    public void Get_UnknownId_ReturnsEmptyList()
    {
        var store = new InMemoryModelOverrideStore();
        store.Get("nonexistent").Should().BeEmpty();
    }

    [Fact]
    public void Add_ThenGet_ReturnsOverrides()
    {
        var store = new InMemoryModelOverrideStore();
        var overrides = new[] { new ModelOverride { Id = "gpt-4o", PromptPrice = 1.00m } };

        store.Add("openai", overrides);

        store.Get("openai").Should().HaveCount(1);
        store.Get("openai")[0].PromptPrice.Should().Be(1.00m);
    }

    [Fact]
    public void Get_ReturnsSnapshot_MutatingResultDoesNotAffectStore()
    {
        // Arrange
        var store = new InMemoryModelOverrideStore();
        store.Add("openai", new[] { new ModelOverride { Id = "gpt-4o" } });

        // Act — get the list twice
        var first = store.Get("openai");
        var second = store.Get("openai");

        // Assert — they are not the same reference (snapshot, not live list)
        first.Should().NotBeSameAs(second, "Get returns a snapshot, not the live list");
        first.Should().HaveCount(1);
        second.Should().HaveCount(1);
    }

    [Fact]
    public void Add_IsCaseInsensitive()
    {
        var store = new InMemoryModelOverrideStore();
        store.Add("OpenAI", new[] { new ModelOverride { Id = "gpt-4o" } });

        store.Get("openai").Should().HaveCount(1);
        store.Get("OPENAI").Should().HaveCount(1);
    }

    [Fact]
    public void Add_AppendsToExisting()
    {
        var store = new InMemoryModelOverrideStore();
        store.Add("openai", new[] { new ModelOverride { Id = "gpt-4o" } });
        store.Add("openai", new[] { new ModelOverride { Id = "gpt-35-turbo" } });

        store.Get("openai").Should().HaveCount(2);
    }
}
