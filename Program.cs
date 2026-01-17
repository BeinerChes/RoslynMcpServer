using RoslynMcpServer;

// MCP servers must use stdio for communication
// All logging goes to stderr to avoid interfering with the protocol
Console.Error.WriteLine("RoslynMcpServer starting...");

var server = new McpServer();

// Register all tools
RoslynTools.RegisterAll(server);

await server.RunAsync();
