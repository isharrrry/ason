# Calling an ASON bridge from Python

The bridge is a gRPC contract before it is a .NET library: `ason_bridge.proto` is the whole interface, and any
language that can compile it can drive an ASON application. This directory holds a runnable example.

## Setup

```bash
python -m pip install -r samples/python/requirements.txt
```

`grpcio-tools` supplies `protoc`; the script compiles the contract into `samples/python/.gen/` on first run, so
there is no code generation step to remember. Use `--proto` to point at another copy of the contract — for
example the one inside the NuGet package (`$(PkgAson_Bridge_Grpc)/protos/ason_bridge.proto`).

## Run

Start an application side first — `samples/ConsoleBridgeAppSample` (formerly `ConsoleGrpcBridgeHost`) listens
on `http://localhost:5222` — then:

```bash
# what does this application expose? (the manifest is the contract)
python samples/python/ason_bridge_client.py --url http://localhost:5222 manifest

# one precise call, no script text
python samples/python/ason_bridge_client.py --url http://localhost:5222 call LibDemoOperator.Add --args "[40, 2]"

# a whole script, with the application's logs streamed back
python samples/python/ason_bridge_client.py --url http://localhost:5222 script "return LibDemoOperator.Add(40, 2);" --stream
```

Other useful subcommands: `instances` (live operator instances and their handles), `mcp <server> <tool>` (a tool
on an MCP server the application itself consumes), `script --fresh-instances` (body-only mode: the application
supplies its proxy layer and today's instance declarations) and `--header Name=Value` (repeatable) for a bridge
that requires authorization.

## What this shows

* **Discovery is the manifest.** The client learns the operator API, the capabilities and the live instances by
  asking; it never holds a second copy of the API. What it may call is whatever the application enabled.
* **Both execution interfaces are reachable from any language.** `call` maps to `InvokeFunction`, `script` to
  `ExecuteScript`; they are independent, and the error codes (`not-supported`, `operator-not-found`,
  `handle-required`, …) mean the same thing here as in the .NET clients.
* **Nothing about the application crosses the boundary.** Python sends a method name and JSON arguments; the
  operators, their data and their credentials stay in the application process.
