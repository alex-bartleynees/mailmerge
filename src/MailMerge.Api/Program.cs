using MailMerge.Api.Host;

var builder = WebApplication.CreateBuilder(args);
builder.AddMailMerge();

var app = builder.Build();
app.UseMailMerge();
app.Run();

// Exposed so integration tests can bootstrap the real host via WebApplicationFactory<Program>.
public partial class Program;
