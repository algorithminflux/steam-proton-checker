using System.Net;

namespace SteamProtonChecker.Tests.TestHelpers;

/// <summary>An HttpMessageHandler that answers from a delegate, so tests never touch the network.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<string> RequestedUrls { get; } = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public static FakeHttpMessageHandler Returning(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => Json(body, status));

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (RequestedUrls)
        {
            RequestedUrls.Add(request.RequestUri!.AbsoluteUri);
        }

        return Task.FromResult(_responder(request));
    }
}
