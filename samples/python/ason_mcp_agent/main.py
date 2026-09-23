#!/usr/bin/env python3
"""Drive an ASON application through MCP with a model: an automated tool-calling test.

The point of this program is not "the connection works" but "the model really called the application's operator
and used its answer". It asks the application for its MCP tools, hands them to OpenAI as function definitions,
executes whichever tool the model chooses (feeding the result back), and repeats until the model answers. The
transcript is printed, and `--expect` turns the run into a test: it fails unless the operator's output appears
in the answer.

    python samples/python/ason_mcp_agent/main.py \
        --instruction "Add 40 and 2 using the application, then tell me the result." \
        --expect 42

Configuration comes from flags or from the repository's usual environment variables:

    --base-url  MY_OPEN_AI_BASE_URL      --model  MY_OPEN_AI_MODEL      --api-key  MY_OPEN_AI_KEY

Without an api key the program exits with code 2 and says so - it never pretends to have run.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "ason_mcp_caller"))

from main import DEFAULT_RELAY, connect_tools, initialize  # noqa: E402  (sibling helper module)


def build_transport(args):
    """Reuses the caller's transports so the agent and the plain caller cannot drift apart."""
    return connect_tools(args.transport, args.url, args.http_url, args.relay, args.key, args.header)


def openai_tools(tools: list[dict]) -> list[dict]:
    """MCP tool descriptors are already JSON Schema, which is what function calling wants."""
    return [{
        "type": "function",
        "function": {
            "name": tool["name"],
            "description": tool.get("description") or "",
            "parameters": tool.get("inputSchema") or {"type": "object", "properties": {}},
        },
    } for tool in tools]


def run_instruction(client, tools: list[dict], instruction: str, model: str, max_rounds: int, verbose: bool) -> str:
    messages = [
        {"role": "system", "content": (
            "You drive a running application through its MCP tools. Call a tool whenever the request needs the "
            "application's data or operators, then answer in one short sentence using the result. Never invent a "
            "result you did not receive from a tool."
        )},
        {"role": "user", "content": instruction},
    ]

    for round_number in range(1, max_rounds + 1):
        completion = client.chat.completions.create(model=model, messages=messages, tools=openai_tools(tools), temperature=0)
        message = completion.choices[0].message
        messages.append(message.model_dump(exclude_none=True))

        calls = message.tool_calls or []
        if not calls:
            return message.content or ""

        for call in calls:
            name = call.function.name
            try:
                arguments = json.loads(call.function.arguments or "{}")
            except json.JSONDecodeError:
                arguments = {}
            if verbose:
                print(f"  round {round_number}: {name}({json.dumps(arguments, ensure_ascii=False)})", file=sys.stderr)

            reply = mcp_request(client, "tools/call", {"name": name, "arguments": arguments})
            if "error" in reply:
                text = json.dumps(reply["error"], ensure_ascii=False)
            else:
                result = reply.get("result", {})
                text = "".join(part.get("text", "") for part in result.get("content", []) if part.get("type") == "text")
                if result.get("isError"):
                    text = f"error: {text}"
            if verbose:
                print(f"  -> {text[:400]}", file=sys.stderr)
            messages.append({"role": "tool", "tool_call_id": call.id, "content": text or "(empty result)"})

    return f"the model did not finish within {max_rounds} rounds"


def mcp_request(client, method: str, params: dict) -> dict:
    """The transports in ason_mcp_caller share one request/notify surface; both work here."""
    return client.request(method, params)


def main() -> int:
    parser = argparse.ArgumentParser(description="Drive an ASON application over MCP with a model.")
    parser.add_argument("--instruction", action="append", default=[], metavar="TEXT",
                        help="what to ask the model (repeatable: each one is a separate case)")
    parser.add_argument("--expect", action="append", default=[], metavar="TEXT",
                        help="the answer must contain this text (repeatable, positional with --instruction)")
    parser.add_argument("--base-url", default=os.environ.get("MY_OPEN_AI_BASE_URL"))
    parser.add_argument("--api-key", default=os.environ.get("MY_OPEN_AI_KEY"))
    parser.add_argument("--model", default=os.environ.get("MY_OPEN_AI_MODEL") or "gpt-4o-mini")
    parser.add_argument("--max-rounds", type=int, default=6)
    parser.add_argument("--transport", choices=["stdio", "http"], default="http")
    parser.add_argument("--url", default="http://localhost:5222")
    parser.add_argument("--http-url", default="http://localhost:5223/mcp")
    parser.add_argument("--relay", default=str(DEFAULT_RELAY))
    parser.add_argument("--key", default=None)
    parser.add_argument("--header", action="append", default=[], metavar="NAME=VALUE")
    parser.add_argument("--verbose", action="store_true", help="show every tool call and its result")
    args = parser.parse_args()

    if not args.api_key:
        print("no model configured: set MY_OPEN_AI_KEY (and MY_OPEN_AI_BASE_URL / MY_OPEN_AI_MODEL) or pass "
              "--api-key/--base-url/--model", file=sys.stderr)
        return 2

    instructions = args.instruction or ["List what this application can do, then add 40 and 2 with it and tell me the result."]

    try:
        from openai import OpenAI
    except ImportError:
        print("the openai package is missing: python -m pip install -r samples/python/requirements-mcp.txt", file=sys.stderr)
        return 2

    client = build_transport(args)
    try:
        initialize(client, "ason-mcp-agent")
        tools = mcp_request(client, "tools/list", {}).get("result", {}).get("tools", [])
        if not tools:
            print("the application published no MCP tools (are its capabilities enabled?)", file=sys.stderr)
            return 1
        print(f"# {len(tools)} tools: {', '.join(tool['name'] for tool in tools)}")

        llm = OpenAI(api_key=args.api_key, base_url=args.base_url)
        failures = 0
        for index, instruction in enumerate(instructions):
            print(f"\n--- instruction: {instruction}")
            answer = run_instruction(llm, tools, instruction, args.model, args.max_rounds, args.verbose)
            print(f"--- answer: {answer}")
            if index < len(args.expect):
                expected = args.expect[index]
                if expected not in answer:
                    print(f"--- FAILED: expected '{expected}' in the answer", file=sys.stderr)
                    failures += 1
        return 1 if failures else 0
    finally:
        client.close()


if __name__ == "__main__":
    raise SystemExit(main())
