namespace Tornado.Services;

public interface IExampleService
{
    ExampleResponse GetExample();
}

public sealed class ExampleService : IExampleService
{
    public ExampleResponse GetExample()
    {
        return new ExampleResponse(
            "tornado",
            "Example endpoint is live.",
            DateTimeOffset.UtcNow
        );
    }
}

public sealed record ExampleResponse(string App, string Message, DateTimeOffset Timestamp);
