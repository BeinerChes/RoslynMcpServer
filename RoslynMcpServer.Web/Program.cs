using RoslynMcpServer.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGraphApi();

var url = "http://localhost:5200";
app.Urls.Add(url);

Console.WriteLine($"Graph visualization server starting at {url}");
app.Run();
