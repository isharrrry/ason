#!/usr/bin/env python3
"""Call an ASON bridge from Python over gRPC.

This is the non-.NET caller path: an application publishes a bridge (`samples/ConsoleBridgeAppSample` or the
WPF demo app), and any language that can compile `ason_bridge.proto` can drive it with no ASON runtime, no
model and no knowledge of the application's types.

The script compiles the contract on first use (`grpc_tools.protoc`) into `.gen/` next to this file, so a fresh
checkout needs nothing but the requirements installed:

    python -m pip install -r samples/python/requirements.txt

Examples (start `samples/ConsoleBridgeAppSample` first, it listens on 5222):

    python samples/python/ason_bridge_client.py --url http://localhost:5222 manifest
    python samples/python/ason_bridge_client.py --url http://localhost:5222 instances
    python samples/python/ason_bridge_client.py --url http://localhost:5222 call LibDemoOperator.Add --args "[40, 2]"
    python samples/python/ason_bridge_client.py --url http://localhost:5222 script "return LibDemoOperator.Add(40, 2);"
    python samples/python/ason_bridge_client.py --url http://localhost:5222 script "return 1;" --stream
    python samples/python/ason_bridge_client.py --url http://localhost:5222 mcp filesystem read_file --args '{"path": "/tmp/x"}'

Notes:

* `--proto` points at another copy of the contract; by default the one in this repository is used. A .NET
  consumer of the NuGet package finds the same file at `$(PkgAson_Bridge_Grpc)/protos/ason_bridge.proto`.
* `--header Name=Value` (repeatable) is how an authorized bridge is called; the application side enables that
  with `AddAsonGrpcBridge(runtime, "<policy>")`.
* What a caller may call is still decided by the manifest: this program is a client, not a second copy of the
  application's API.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
GEN = HERE / ".gen"
DEFAULT_PROTO = HERE.parent.parent / "src" / "Ason.Bridge.Grpc" / "Protos" / "ason_bridge.proto"

# A Windows console usually hands Python a legacy codepage (GBK, cp1252, ...), and this program prints the
# manifest - descriptions included - as JSON. Force UTF-8 on the streams it writes to.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8")
    except (AttributeError, ValueError):
        pass


def ensure_stubs(proto: Path) -> None:
    """Compiles the contract into .gen/ when it is missing or older than the .proto file.

    The directory goes on sys.path either way: a run that reuses an up-to-date .gen/ would otherwise generate
    nothing and then fail to import what is already there.
    """
    generated = GEN / "ason_bridge_pb2.py"
    if generated.exists() and generated.stat().st_mtime >= proto.stat().st_mtime:
        sys.path.insert(0, str(GEN))
        return
    GEN.mkdir(exist_ok=True)
    try:
        import grpc_tools.protoc  # noqa: F401  (importing proves the tool is installed)
    except ImportError:
        sys.exit("grpcio-tools is missing: python -m pip install -r samples/python/requirements.txt")
    command = [
        sys.executable, "-m", "grpc_tools.protoc",
        f"-I{proto.parent}",
        f"--python_out={GEN}",
        f"--grpc_python_out={GEN}",
        proto.name,
    ]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"protoc failed:\n{result.stderr}")
    sys.path.insert(0, str(GEN))


def load_json(value: str, what: str):
    """Reads inline JSON, or a file when the value starts with '@'.

    The file form exists because shells rewrite nested quotes: `--args "[40, 2]"` survives PowerShell, but
    `--args '{"path": "/tmp"}'` does not - the quotes never reach Python. `--args @args.json` always does.
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


def open_channel(target: str, headers: dict[str, str]):
    import grpc

    if target.startswith("https://"):
        credentials = grpc.ssl_channel_credentials()
        channel = grpc.secure_channel(target[len("https://"):], credentials)
    else:
        # A bridge listens on h2c (HTTP/2 without TLS) by default, so the scheme is not part of the address.
        channel = grpc.insecure_channel(target[len("http://"):] if target.startswith("http://") else target)
    metadata = tuple(headers.items()) or None
    return channel, metadata


def show_result(result) -> int:
    """Prints an ExecuteResult the same way the .NET clients report it, and returns a process exit code."""
    if result.success:
        print(result.json if result.json else "null")
        return 0
    print(f"error [{result.error_code or 'execution-failed'}]: {result.error}", file=sys.stderr)
    return 1


def split_target(operator: str, method: str | None) -> tuple[str, str]:
    """Accepts both `call Operator Method` and the dotted `call Operator.Method` the README shows."""
    if method:
        return operator, method
    name, _, last = operator.rpartition(".")
    if not name:
        sys.exit(f"'{operator}' is not an operator method: write 'Operator.Method' (or pass the method separately)")
    return name, last


def read_code(value: str) -> str:
    """The script body, or the contents of a file when it starts with '@' - a long script does not fit an argv."""
    if not value.startswith("@"):
        return value
    path = Path(value[1:]).resolve()
    if not path.exists():
        sys.exit(f"script file not found: {path}")
    return path.read_text(encoding="utf-8-sig")


def main() -> int:
    parser = argparse.ArgumentParser(description="Call an ASON bridge over gRPC.")
    parser.add_argument("--url", default="http://localhost:5222", help="gRPC address of the bridge")
    parser.add_argument("--proto", default=str(DEFAULT_PROTO), help="path to ason_bridge.proto")
    parser.add_argument("--header", action="append", default=[], metavar="NAME=VALUE",
                        help="metadata sent with every call (repeatable), for an authorized bridge")
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("manifest", help="print the manifest (the contract the application publishes)")
    sub.add_parser("instances", help="print the live operator instances")

    call = sub.add_parser("call", help="call one operator method (single-function interface)")
    call.add_argument("operator", help="'Operator' or 'Operator.Method' (the .NET caller sample uses the dotted form)")
    call.add_argument("method", nargs="?", default=None, help="the method, when the operator was given on its own")
    call.add_argument("--args", default="[]", help="JSON array of arguments in parameter order, or @file.json")
    call.add_argument("--handle", default=None, help="handle, when several instances of the operator exist")

    script = sub.add_parser("script", help="run a complete script (whole-script interface)")
    script.add_argument("code", help="the script body, or @file.txt to read it from a file")
    script.add_argument("--no-preamble", action="store_true",
                        help="send the code as it is instead of letting the application prepend its proxy layer")
    script.add_argument("--fresh-instances", action="store_true",
                        help="body-only mode: the application supplies its proxy layer and today's instance declarations")
    script.add_argument("--stream", action="store_true", help="print the application's logs while the script runs")

    mcp = sub.add_parser("mcp", help="pass through to a tool on an MCP server the application consumes")
    mcp.add_argument("server")
    mcp.add_argument("tool")
    mcp.add_argument("--args", default="{}", help="JSON object of the tool's parameters, or @file.json")

    args = parser.parse_args()

    proto = Path(args.proto).resolve()
    if not proto.exists():
        sys.exit(f"contract not found: {proto} (pass --proto)")
    ensure_stubs(proto)

    import grpc  # noqa: E402
    import ason_bridge_pb2 as pb  # noqa: E402
    import ason_bridge_pb2_grpc as pb_grpc  # noqa: E402

    headers = {}
    for header in args.header:
        name, _, value = header.partition("=")
        if not name or not _:
            sys.exit(f"--header expects NAME=VALUE, got '{header}'")
        headers[name] = value

    channel, metadata = open_channel(args.url, headers)
    stub = pb_grpc.AsonBridgeStub(channel)

    try:
        if args.command == "manifest":
            reply = stub.GetManifest(pb.GetManifestRequest(), metadata=metadata)
            manifest = json.loads(reply.manifest_json)
            manifest["_protocolVersion"] = reply.protocol_version
            manifest["_instancesRevision"] = reply.instances_revision
            print(json.dumps(manifest, indent=2, ensure_ascii=False))
            return 0

        if args.command == "instances":
            reply = stub.ListInstances(pb.ListInstancesRequest(), metadata=metadata)
            print(json.dumps([{"handle": i.handle, "typeName": i.type_name, "initialized": i.initialized}
                              for i in reply.instances], indent=2, ensure_ascii=False))
            return 0

        if args.command == "call":
            operator, method = split_target(args.operator, args.method)
            request = pb.InvokeFunctionRequest(
                operator=operator,
                method=method,
                arguments_json=json.dumps(load_json(args.args, "--args")),
                handle=args.handle or "",
            )
            return show_result(stub.InvokeFunction(request, metadata=metadata))

        if args.command == "script":
            code = read_code(args.code)
            request = pb.ExecuteScriptRequest(
                code=code,
                include_proxy_preamble=not args.no_preamble,
                include_instance_declarations=args.fresh_instances,
            )
            if not args.stream:
                return show_result(stub.ExecuteScript(request, metadata=metadata))

            # The server streams the application's logs, then exactly one result or error event.
            result = None
            for event in stub.StreamExecution(request, metadata=metadata):
                if event.type == "log":
                    print(f"[{event.level}] {event.message}", file=sys.stderr)
                else:
                    result = event.result
            if result is None:
                sys.exit("the stream ended without a result")
            return show_result(result)

        if args.command == "mcp":
            request = pb.InvokeMcpToolRequest(
                server=args.server,
                tool=args.tool,
                arguments_json=json.dumps(load_json(args.args, "--args")),
            )
            return show_result(stub.InvokeMcpTool(request, metadata=metadata))

        parser.error(f"unknown command {args.command}")
        return 2
    except grpc.RpcError as error:
        # An unimplemented capability and an authorization failure are both normal answers, not crashes.
        sys.exit(f"{error.code().name}: {error.details()}")
    finally:
        channel.close()


if __name__ == "__main__":
    raise SystemExit(main())
