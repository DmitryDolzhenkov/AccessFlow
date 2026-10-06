namespace AccessFlow.SystemStub;

// An explicit entry point: the top-level one would be a public global Program,
// which clashes with the API's in the tests that reference both.
internal static class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        builder.Services.AddSystemStub();

        var app = builder.Build();
        app.MapSystemStub();

        app.Run();
    }
}
