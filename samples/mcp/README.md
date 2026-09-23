# Connecting a real MCP client to an ASON application

Two files and one command are everything a stdio-only client needs. Start an application side first:

```bash
dotnet run --project samples/ConsoleBridgeAppSample          # gRPC :5222, MCP :5223/mcp
# or, for the WPF application:
dotnet run --project samples/WpfAppOnlyDemo
```

Then pick the shape that matches your client.

## 1. A client that starts a stdio MCP server (Claude Desktop, or any `mcpServers` config)

`claude_desktop_config.json` in this directory is the whole recipe. Replace `<repo>` with the absolute path of
your checkout, merge the `mcpServers` block into the client's own config, and restart the client:

```json
{
  "mcpServers": {
    "ason-application": {
      "command": "dotnet",
      "args": ["exec", "<repo>/src/bin/Release/net9.0/Ason.Bridge.McpHost.dll",
               "--url", "http://localhost:5222"]
    }
  }
}
```

Build the relay once, so the path exists: `dotnet build src/Ason.Bridge.McpHost -c Release`. Note where the file
lands: the projects in this repository share one output root, so it is `src/bin/Release/net9.0/` — not
`src/Ason.Bridge.McpHost/bin/...`. If your build puts it elsewhere, `find . -name Ason.Bridge.McpHost.dll` tells
you, and `samples/python/ason_mcp_caller` accepts `--relay <path>` to check the result before you touch a desktop
client.

Why a relay at all: stdio MCP means *the client starts the server*, and a running desktop application cannot be
that child process. The relay is the child; it connects to the application over gRPC (or over MCP, with
`--transport mcp`) and republishes the same tools. If your application requires authorization, add the
credentials to the relay's command line:

```json
"args": ["exec", "<repo>/.../Ason.Bridge.McpHost.dll", "--url", "http://localhost:5222", "--key", "<the key>"]
```

`--header Name=Value` does the same for any other header, and `ASON_BRIDGE_KEY` works as an environment
variable instead of an argument.

## 2. A client that speaks Streamable HTTP MCP

No relay and no launch command — the application publishes MCP itself, so the client only needs the URL
(`http://localhost:5223/mcp`). `http_mcp_config.json` is the equivalent fragment.

## 3. What the client sees

The tools the application publishes depend on what it enabled (`AsonBridgeCapabilities`):

| Tool | What it does |
|---|---|
| `ason_get_manifest` | The whole contract: protocol version, execution location, capabilities, operator API, live instances |
| `ason_get_script_api` | The generated proxy layer, the signature listing and the Markdown listing, for writing a script |
| `ason_list_instances` | The operator instances alive right now, with the handle that addresses each one |
| `ason_execute_script` | Runs a script body against the application |
| `ason_stream_script` | Runs a script and returns its logs with the result (present when log streaming is on) |
| `ason_invoke_function` | Calls exactly one operator method, without any script text |
| `ason_invoke_mcp_tool` | Passes through to an MCP server the *application* consumes (off by default) |

A disabled capability simply has no tool — an MCP client that lists tools sees the truth, and the manifest says
the same thing.

## 4. Without an MCP client at all

`samples/python/ason_mcp_caller` is a minimal MCP client (stdlib only): it starts the relay over stdio — or
talks to the HTTP endpoint — and offers `--list` and `--call`. It is also the fastest way to check a
configuration before pointing a desktop client at it.
