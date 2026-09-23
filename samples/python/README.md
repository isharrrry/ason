# Calling an ASON bridge from Python

The bridge is a gRPC contract before it is a .NET library: `ason_bridge.proto` is the whole interface, and any
language that can compile it can drive an ASON application. This directory holds a runnable example — plus two
programs that need no gRPC at all (see the table at the end).

## Setup

```bash
python -m pip install -r samples/python/requirements.txt
```

On Windows, add `--user` if pip cannot write to the system Python (`pip install --user -r …`). Behind a slow or
blocked PyPI, any mirror works — for example Tsinghua:

```bash
python -m pip install -i https://pypi.tuna.tsinghua.edu.cn/simple -r samples/python/requirements.txt
```

`grpcio-tools` supplies `protoc`; the script compiles the contract into `samples/python/.gen/` on first run, so
there is no code generation step to remember. Use `--proto` to point at another copy of the contract — for
example the one inside the NuGet package (`$(PkgAson_Bridge_Grpc)/protos/ason_bridge.proto`).

## Run

Start an application side first — `samples/ConsoleBridgeAppSample` listens on `http://localhost:5222` — then:

```bash
# what does this application expose? (the manifest is the contract)
python samples/python/ason_bridge_client.py --url http://localhost:5222 manifest

# one precise call, no script text
python samples/python/ason_bridge_client.py --url http://localhost:5222 call LibDemoStaticOperator.Add --args "[40, 2]"

# a whole script, with the application's logs streamed back
python samples/python/ason_bridge_client.py --url http://localhost:5222 script "return LibDemoStaticOperator.Add(40, 2);" --stream
```

`call` accepts both `Operator.Method` and `Operator Method`, and `--args` accepts `@file.json` instead of inline
JSON — worth knowing on Windows PowerShell, which rewrites the quotes inside an argument before Python ever sees
it: `--args @args.json` always arrives intact.

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

## The other two programs here

| Path | What it is | Dependencies |
|---|---|---|
| `ason_mcp_caller/main.py` | A minimal **MCP** client: starts the stdio relay (or talks to the HTTP MCP endpoint) and offers `--list` / `--call`. Use it to check an MCP configuration before pointing a desktop client at it. | standard library only |
| `ason_mcp_agent/main.py` | A **model driving the application over MCP**: it hands the application's MCP tools to OpenAI as functions, runs whichever tool the model picks, and feeds the result back. `--instruction "…" --expect 42` turns it into an automated tool-calling test. | `openai` (`requirements-mcp.txt`) |

```bash
python -m pip install -r samples/python/requirements-mcp.txt

# see what an application publishes over MCP, without any desktop client
#   stdio starts the relay process itself; http talks to the application's own /mcp endpoint
python samples/python/ason_mcp_caller/main.py --transport stdio --url http://localhost:5222 --list
python samples/python/ason_mcp_caller/main.py --transport http --http-url http://localhost:5223/mcp --list

# call one operator through MCP (--args @file.json avoids shell quoting problems)
python samples/python/ason_mcp_caller/main.py --transport http --http-url http://localhost:5223/mcp \
  --call ason_invoke_function --args '{"operator":"LibDemoStaticOperator","method":"Add","argumentsJson":"[40,2]"}'

# let a model use it: the tool call has to really happen, or --expect fails the run
MY_OPEN_AI_KEY=… python samples/python/ason_mcp_agent/main.py \
  --instruction "Add 40 and 2 with the application's operator and tell me the result." --expect 42
```

`--base-url` and `--model` point the agent at any OpenAI-compatible endpoint, so a domestic provider works
without changing code — for example `--base-url https://api.deepseek.com --model deepseek-chat`. `--instruction`
may be repeated (one case each) and `--expect` pairs with it positionally: with `--expect 42` the run fails
unless the number the *application* returned appears in the model's answer, which is what makes this a test of
tool calling rather than of connectivity. `--verbose` prints every tool call and its result to stderr.

A run looks like this (application side: `samples/ConsoleBridgeAppSample`):

```
# 6 tools: ason_list_instances, ason_get_manifest, ason_invoke_function, ason_stream_script, ason_execute_script, ason_get_script_api
--- instruction: Use the application to add 40 and 2, then tell me the result.
--- answer: LibDemoStaticOperator.Add(40, 2) returned 42.
```

Give the model room to be wrong: on an instruction where it invents an operator name it receives the
application's `operator-not-found` result, reads the manifest with `ason_get_manifest`, and calls the right one -
the loop feeds every result (including errors) back to the model.

Only the first program needs gRPC. The MCP caller uses nothing but the standard library (JSON-RPC over the relay's
stdio, or Streamable HTTP over `urllib`), and the agent adds just the `openai` client — the MCP tools it hands to
the model are the application's own, so a deployment can be driven from Python without a `.proto` file at all.

The copy-pasteable client configurations these programs mirror live in [`samples/mcp`](../mcp/README.md).
