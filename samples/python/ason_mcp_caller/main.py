#!/usr/bin/env python3
"""A minimal MCP client for an ASON application: stdlib only, no MCP SDK.

It exists so the stdio path is checkable without a desktop client, and so the shape of the protocol is visible
in one file. Two transports:

  stdio  - starts the relay (`Ason.Bridge.McpHost`) as a child and speaks JSON-RPC over its stdin/stdout
  http   - talks to an application that publishes MCP itself (`http://localhost:5223/mcp`)

Examples (start `samples/ConsoleBridgeAppSample` first):

    python samples/python/ason_mcp_caller/main.py --transport http --list
    python samples/python/ason_mcp_caller/main.py --transport http --call ason_invoke_function \
        --args '{"operator": "LibDemoStaticOperator", "method": "Add", "argumentsJson": "[40,2]"}'
    python samples/python/ason_mcp_caller/main.py --transport stdio --list
    python samples/python/ason_mcp_caller/main.py --transport stdio --call ason_get_manifest

A stdio relay that needs authorization takes `--key` (or `--header Name=Value`), which is passed straight to it.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent.parent


def find_relay() -> Path:
    """The relay's built assembly, wherever this repository puts it.

    The projects share one output root (`src/bin/<configuration>/<tfm>/`), while a plain `dotnet build` of a
    single project can also leave it next to that project; both are checked so the sample works either way.
    """
    for configuration in ("Release", "Debug"):
        for candidate in (
            REPO / "src" / "bin" / configuration / "net9.0" / "Ason.Bridge.McpHost.dll",
            REPO / "src" / "Ason.Bridge.McpHost" / "bin" / configuration / "net9.0" / "Ason.Bridge.McpHost.dll",
        ):
            if candidate.exists():
                return candidate
    return REPO / "src" / "bin" / "Release" / "net9.0" / "Ason.Bridge.McpHost.dll"


DEFAULT_RELAY = find_relay()
PROTOCOL_VERSION = "2024-11-05"


class StdioServer:
    """MCP over a child process's stdin/stdout: one JSON-RPC message per line."""

    def __init__(self, relay: Path, url: str, extra: list[str]):
        if not relay.exists():
            sys.exit(f"relay not found: {relay} (build it: dotnet build src/Ason.Bridge.McpHost -c Release)")
        command = ["dotnet", "exec", str(relay), "--url", url, *extra]
        self._process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                         stderr=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)
        self._id = 0

    def request(self, method: str, params: dict | None = None) -> dict:
        self._id += 1
        message = {"jsonrpc": "2.0", "id": self._id, "method": method}
        if params is not None:
            message["params"] = params
        self._process.stdin.write(json.dumps(message) + "\n")
        self._process.stdin.flush()
        while True:
            line = self._process.stdout.readline()
            if not line:
                error = self._process.stderr.read()
                sys.exit(f"the relay closed the connection: {error.strip()}")
            reply = json.loads(line)
            if reply.get("id") == self._id:
                return reply

    def notify(self, method: str, params: dict | None = None) -> None:
        message = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            message["params"] = params
        self._process.stdin.write(json.dumps(message) + "\n")
        self._process.stdin.flush()

    def close(self) -> None:
        self._process.terminate()


class HttpServer:
    """MCP over Streamable HTTP: a POST per message, with the session id the server hands back."""

    def __init__(self, url: str, headers: dict[str, str]):
        self._url = url
        self._headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream", **headers}
        self._session: str | None = None
        self._id = 0

    def request(self, method: str, params: dict | None = None) -> dict:
        self._id += 1
        message = {"jsonrpc": "2.0", "id": self._id, "method": method}
        if params is not None:
            message["params"] = params
        return self._post(message)

    def notify(self, method: str, params: dict | None = None) -> None:
        message = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            message["params"] = params
        self._post(message, expect_reply=False)

    def close(self) -> None:
        return None

    def _post(self, message: dict, expect_reply: bool = True) -> dict:
        headers = dict(self._headers)
        if self._session:
            headers["Mcp-Session-Id"] = self._session
        request = urllib.request.Request(self._url, data=json.dumps(message).encode("utf-8"), headers=headers, method="POST")
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                if response.headers.get("Mcp-Session-Id"):
                    self._session = response.headers["Mcp-Session-Id"]
                body = response.read().decode("utf-8")
                content_type = response.headers.get("Content-Type", "")
        except urllib.error.HTTPError as error:
            sys.exit(f"HTTP {error.code}: {error.read().decode('utf-8', 'replace')}")
        if not expect_reply or not body.strip():
            return {}
        # A streamable-HTTP server may answer with server-sent events; the JSON-RPC reply is in a data line.
        if "text/event-stream" in content_type:
            for line in body.splitlines():
                if line.startswith("data:"):
                    return json.loads(line[len("data:"):].strip())
            sys.exit(f"the server sent an event stream without a data line: {body!r}")
        return json.loads(body)


def load_json(value: str, what: str):
    """Reads inline JSON, or a file when the value starts with '@'.

    A shell rewrites nested quotes before the program sees them: `--args "[2, 3]"` survives PowerShell, but
    `--args '{"path": "/tmp"}'` loses its quotes. `--args @args.json` always arrives intact.
    """
    if value.startswith("@"):
        path = Path(value[1:]).resolve()
        if not path.exists():
            sys.exit(f"{what}: file not found: {path}")
        value = path.read_text(encoding="utf-8-sig")
    try:
        return json.loads(value)
    except json.JSONDecodeError as error:
        sys.exit(f"{what} is not valid JSON ({error}); use --args @file.json if your shell rewrites quotes")


def connect_tools(transport: str, url: str, http_url: str, relay: str, key: str | None, headers: list[str]) -> object:
    """Builds a transport without argparse, so the agent in ../ason_mcp_agent uses the very same ones."""
    if transport == "stdio":
        extra = []
        if key:
            extra += ["--key", key]
        for header in headers:
            extra += ["--header", header]
        return StdioServer(Path(relay).resolve(), url, extra)

    header_map = {}
    if key:
        header_map["Authorization"] = f"Bearer {key}"
    for header in headers:
        name, _, value = header.partition("=")
        header_map[name] = value
    return HttpServer(http_url, header_map)


def connect(args) -> object:
    return connect_tools(args.transport, args.url, args.http_url, args.relay, args.key, args.header)


def initialize(server, name: str) -> dict:
    reply = server.request("initialize", {
        "protocolVersion": PROTOCOL_VERSION,
        "capabilities": {},
        "clientInfo": {"name": name, "version": "0.9.0"},
    })
    server.notify("notifications/initialized")
    return reply


def unwrap(reply: dict) -> tuple[bool, str]:
    """Turns a JSON-RPC reply into (ok, text), the way every MCP client has to."""
    if "error" in reply:
        return False, json.dumps(reply["error"], ensure_ascii=False)
    result = reply.get("result", {})
    text = "".join(part.get("text", "") for part in result.get("content", []) if part.get("type") == "text")
    return not result.get("isError", False), text


def main() -> int:
    parser = argparse.ArgumentParser(description="A minimal MCP client for an ASON application.")
    parser.add_argument("--transport", choices=["stdio", "http"], default="http")
    parser.add_argument("--url", default="http://localhost:5222", help="gRPC address the stdio relay connects to")
    parser.add_argument("--http-url", default="http://localhost:5223/mcp", help="MCP endpoint for the http transport")
    parser.add_argument("--relay", default=str(DEFAULT_RELAY), help="path to Ason.Bridge.McpHost.dll")
    parser.add_argument("--key", default=None, help="authorization for a bridge that requires it")
    parser.add_argument("--header", action="append", default=[], metavar="NAME=VALUE", help="extra header (repeatable)")
    parser.add_argument("--list", action="store_true", help="print the tools the application publishes")
    parser.add_argument("--call", metavar="TOOL", help="call one tool")
    parser.add_argument("--args", default="{}", help="JSON object of the tool's arguments, or @file.json")
    args = parser.parse_args()

    server = connect(args)
    try:
        handshake = initialize(server, "ason-mcp-caller")
        version = handshake.get("result", {}).get("serverInfo", {}).get("name", "?")
        if args.list or not args.call:
            # tools/list answers with a schema list, not with content parts, so it is read directly.
            reply = server.request("tools/list")
            if "error" in reply:
                print(json.dumps(reply["error"], ensure_ascii=False), file=sys.stderr)
                return 1
            tools = reply.get("result", {}).get("tools", [])
            print(f"# {version}: {len(tools)} tools")
            for tool in tools:
                description = (tool.get("description") or "").splitlines()
                print(f"- {tool['name']}: {description[0] if description else ''}")
            return 0

        ok, text = unwrap(server.request("tools/call", {"name": args.call, "arguments": load_json(args.args, "--args")}))
        print(text)
        return 0 if ok else 1
    finally:
        server.close()


if __name__ == "__main__":
    raise SystemExit(main())
