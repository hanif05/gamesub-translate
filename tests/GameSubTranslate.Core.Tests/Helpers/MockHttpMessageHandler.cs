using System.Net;

namespace GameSubTranslate.Core.Tests.Helpers;

/// <summary>
/// Stubbed HttpMessageHandler — register a delegate that returns a canned response,
/// inspect the captured request, and assert against HitCount.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; }
        = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });

    public int HitCount { get; private set; }
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public List<HttpRequestMessage> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HitCount++;
        LastRequest = request;
        Requests.Add(request);
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        return await Respond(request);
    }

    /// <summary>Queue responses FIFO. Each call consumes one. Useful for retry-then-success scenarios.</summary>
    public void QueueResponses(params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        Respond = _ => Task.FromResult(queue.Dequeue());
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body) { Headers = { ContentType = new("application/json") } } };
}
