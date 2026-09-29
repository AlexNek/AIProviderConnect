namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// An immutable record of one outgoing request: the <c>Authorization</c> header parameter
/// (null when the request carried none) and the request host. <see cref="CapturingHttpMessageHandler"/>
/// records a snapshot while serving each request, so concurrent calls can be asserted after the
/// provider has disposed the underlying <see cref="HttpRequestMessage"/>.
/// </summary>
public sealed record CapturedRequestSnapshot(string? Authorization, string? Host);
