using FluentAssertions;

using ScraperTool.Services.UrlResearch;

namespace ScraperTool.Tests;

public class ResearchSessionTests
{
    [Fact]
    public void Clear_RemovesAllFindings()
    {
        var session = new ResearchSession();
        session.RecordFinding("website", "https://openai.com", "reason1");

        session.Clear();

        session.GetAllFindings().Should().BeEmpty();
    }

    [Fact]
    public void FindingsAreCaseInsensitive()
    {
        var session = new ResearchSession();

        session.RecordFinding("Website", "https://OpenAI.com", "reason");

        session.HasFinding("website", "https://openai.com").Should().BeTrue();
    }

    [Fact]
    public void GetAllFindings_ReturnsAllRecordedFindings()
    {
        var session = new ResearchSession();
        session.RecordFinding("website", "https://openai.com", "reason1");
        session.RecordFinding("baseUrl", "https://api.openai.com/v1", "reason2");

        var all = session.GetAllFindings();
        all.Should().HaveCount(2);
        all.Should().ContainKey("website:https://openai.com");
        all.Should().ContainKey("baseUrl:https://api.openai.com/v1");
    }

    [Fact]
    public void HasFinding_ReturnsFalseForUnknownFieldValue()
    {
        var session = new ResearchSession();

        session.HasFinding("loginUrl", "https://example.com").Should().BeFalse();
    }

    [Fact]
    public void RecordFinding_StoresAndRetrievesFinding()
    {
        var session = new ResearchSession();

        session.RecordFinding("website", "https://openai.com", "Root domain used");

        session.HasFinding("website", "https://openai.com").Should().BeTrue();
        session.GetFindingReason("website", "https://openai.com").Should().Be("Root domain used");
    }
}
