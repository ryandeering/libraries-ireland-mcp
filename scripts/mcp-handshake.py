#!/usr/bin/env python3
"""Check that a built server completes an MCP initialize handshake.

Usage: python3 scripts/mcp-handshake.py <path-to-binary>

Holds stdin open until the response arrives, rather than piping a single line and closing it. The
server shuts down on EOF, and a bare `printf ... | server` closes stdin quickly enough that the
process can exit before its queued response is flushed, which makes the check pass or fail on
timing rather than on behaviour.

No third-party dependencies, so it runs on the Linux, macOS and Windows runners alike.
"""

import json
import subprocess
import sys
import threading
import queue

TIMEOUT_SECONDS = 30

REQUEST = {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {
        "protocolVersion": "2025-06-18",
        "capabilities": {},
        "clientInfo": {"name": "handshake-check", "version": "1"},
    },
}

LIST_TOOLS = {"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}}


def fail(message):
    print(f"FAIL: {message}", file=sys.stderr)
    sys.exit(1)


def main():
    if len(sys.argv) != 2:
        fail("usage: mcp-handshake.py <path-to-binary>")

    binary = sys.argv[1]

    proc = subprocess.Popen(
        [binary],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
    )

    lines = queue.Queue()

    def reader():
        for line in proc.stdout:
            lines.put(line)
        lines.put(None)

    threading.Thread(target=reader, daemon=True).start()

    def send(payload):
        proc.stdin.write(json.dumps(payload) + "\n")
        proc.stdin.flush()

    def await_response(request_id):
        while True:
            try:
                line = lines.get(timeout=TIMEOUT_SECONDS)
            except queue.Empty:
                fail(f"no response to id={request_id} within {TIMEOUT_SECONDS}s")
            if line is None:
                fail(f"server exited before responding to id={request_id}")
            line = line.strip()
            if not line:
                continue
            try:
                message = json.loads(line)
            except json.JSONDecodeError:
                fail(f"non-JSON on stdout, which must carry only JSON-RPC: {line[:200]}")
            if message.get("id") == request_id:
                return message

    try:
        send(REQUEST)
        initialize = await_response(1)
        if "error" in initialize:
            fail(f"initialize returned an error: {initialize['error']}")

        server_info = initialize.get("result", {}).get("serverInfo")
        if not server_info:
            fail(f"initialize response carried no serverInfo: {initialize}")

        send({"jsonrpc": "2.0", "method": "notifications/initialized"})
        send(LIST_TOOLS)
        listed = await_response(2)
        tools = listed.get("result", {}).get("tools", [])
        if not tools:
            fail("tools/list returned no tools")

        names = sorted(t["name"] for t in tools)
        print(f"OK: {server_info['name']} {server_info['version']}, {len(tools)} tools: {', '.join(names)}")

        # The write tool must not advertise itself as read-only: clients use readOnlyHint to decide
        # whether a call can run without asking the user first.
        by_name = {t["name"]: t.get("annotations") or {} for t in tools}
        setter = by_name.get("set_home_library")
        if setter is None:
            fail("set_home_library is missing from tools/list")
        if setter.get("readOnlyHint") is not False:
            fail(f"set_home_library writes to disk but advertises readOnlyHint={setter.get('readOnlyHint')}")
        for name in ("search_catalogue", "browse_subject", "get_book", "where_can_i_get_this"):
            if by_name.get(name, {}).get("readOnlyHint") is not True:
                fail(f"{name} is a read but does not advertise readOnlyHint=true")
        print("OK: tool annotations are consistent with what each tool actually does")
    finally:
        try:
            proc.stdin.close()
            proc.wait(timeout=10)
        except Exception:
            proc.kill()


if __name__ == "__main__":
    main()
