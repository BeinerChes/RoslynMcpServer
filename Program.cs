using RoslynMcpServer;

// MCP servers must use stdio for communication
// All logging goes to stderr to avoid interfering with the protocol
Console.Error.WriteLine("RoslynMcpServer starting...");

// Start HTTP server for hook token validation
using var hookServer = new HookValidationServer();
hookServer.Start();

var server = new McpServer();

// Register all tools
RoslynTools.RegisterAll(server);

await server.RunAsync();
