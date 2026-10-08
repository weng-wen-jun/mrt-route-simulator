#!/usr/bin/env python3
"""Black-box stdio JSON-RPC smoke test for the MRT MCP server.

Only Python's standard library is used. The script starts one MCP child,
speaks newline-delimited JSON-RPC 2.0, and terminates that child on exit.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading
import time
from typing import Any


PROTOCOL_VERSION = "2025-11-25"
RESPONSE_TIMEOUT_SECONDS = 60.0
SCRIPT_VERSION = "0.1.0"

# Keep the public contract in one table. --tool-map allows a final server rename
# without changing the transport implementation.
TOOL_NAMES = {
    "server_info": "server_info",
    "project_load": "project_load",
    "project_validate": "project_validate",
    "simulation_advance": "simulation_advance",
    "simulation_snapshot": "simulation_snapshot",
    "simulation_events": "simulation_events",
    "simulation_reset": "simulation_reset",
    "project_save": "project_save",
    "simulation_export_csv": "simulation_export_csv",
    "simulation_timetable": "simulation_timetable",
    "project_get": "project_get",
    "project_replace": "project_replace",
    "desktop_command": "desktop_command",
    "desktop_status": "desktop_status",
}


class TestFailure(RuntimeError):
    pass


class ToolCall:
    def __init__(self, ok: bool, payload: Any = None, text: str | None = None,
                 error: Any = None, raw: dict[str, Any] | None = None) -> None:
        self.ok = ok
        self.payload = payload
        self.text = text
        self.error = error
        self.raw = raw or {}

    @property
    def rejected(self) -> bool:
        if not self.ok:
            return True
        if isinstance(self.payload, dict):
            return (
                self.payload.get("ok") is False
                or self.payload.get("success") is False
                or self.payload.get("error") not in (None, "")
            )
        return False


class JsonRpcClient:
    """Sequential JSON-RPC client; stderr is drained into a bounded tail."""

    def __init__(self, command: list[str], cwd: Path, timeout: float) -> None:
        self.command = command
        self.cwd = cwd
        self.timeout = timeout
        self.process: subprocess.Popen[bytes] | None = None
        self.stdout_queue: queue.Queue[Any] = queue.Queue()
        self.stderr_queue: queue.Queue[str] = queue.Queue(maxsize=256)
        self.write_lock = threading.Lock()
        self.next_id = 1
        self.pending: dict[int, dict[str, Any]] = {}

    def start(self) -> None:
        try:
            self.process = subprocess.Popen(
                self.command,
                cwd=str(self.cwd),
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                bufsize=0,
            )
        except OSError as exc:
            raise TestFailure(f"cannot start MCP child: {exc}") from exc
        assert self.process.stdout is not None
        assert self.process.stderr is not None
        threading.Thread(target=self._read_stdout, name="mcp-stdout", daemon=True).start()
        threading.Thread(target=self._read_stderr, name="mcp-stderr", daemon=True).start()

    def _read_stdout(self) -> None:
        assert self.process is not None and self.process.stdout is not None
        for raw in iter(self.process.stdout.readline, b""):
            try:
                value = json.loads(raw.decode("utf-8-sig"))
            except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                self.stdout_queue.put({"_protocol_error": f"invalid stdout JSON: {exc}"})
                continue
            self.stdout_queue.put(value)

    def _read_stderr(self) -> None:
        assert self.process is not None and self.process.stderr is not None
        for raw in iter(self.process.stderr.readline, b""):
            try:
                self.stderr_queue.put_nowait(raw.decode("utf-8", "replace").rstrip()[-2000:])
            except queue.Full:
                # Keep draining so diagnostics cannot deadlock the child.
                pass

    def stderr_tail(self) -> list[str]:
        result: list[str] = []
        while True:
            try:
                result.append(self.stderr_queue.get_nowait())
            except queue.Empty:
                return result

    def _write(self, message: dict[str, Any]) -> None:
        assert self.process is not None and self.process.stdin is not None
        line = (json.dumps(message, ensure_ascii=False, separators=(",", ":")) + "\n").encode()
        with self.write_lock:
            try:
                self.process.stdin.write(line)
                self.process.stdin.flush()
            except OSError as exc:
                raise TestFailure(f"cannot write MCP request: {exc}") from exc

    def notify(self, method: str, params: dict[str, Any] | None = None) -> None:
        message: dict[str, Any] = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            message["params"] = params
        self._write(message)

    def request(self, method: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        request_id = self.next_id
        self.next_id += 1
        message: dict[str, Any] = {"jsonrpc": "2.0", "id": request_id, "method": method}
        if params is not None:
            message["params"] = params
        self._write(message)
        deadline = time.monotonic() + self.timeout
        while True:
            if request_id in self.pending:
                return self.pending.pop(request_id)
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TestFailure(self._timeout_message(method))
            try:
                response = self.stdout_queue.get(timeout=min(remaining, 0.25))
            except queue.Empty:
                if self.process is not None and self.process.poll() is not None:
                    raise TestFailure(self._exit_message(method))
                continue
            if not isinstance(response, dict):
                raise TestFailure(f"non-object JSON-RPC response for {method}: {response!r}")
            if "_protocol_error" in response:
                raise TestFailure(str(response["_protocol_error"]))
            if response.get("id") == request_id:
                return response
            other_id = response.get("id")
            if isinstance(other_id, int):
                self.pending[other_id] = response

    def request_many(self, requests: list[tuple[str, dict[str, Any]]]) -> list[dict[str, Any]]:
        """Send several requests before reading responses to exercise concurrent MCP dispatch."""
        ids = []
        for method, params in requests:
            request_id = self.next_id
            self.next_id += 1
            ids.append(request_id)
            self._write({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params})
        responses = {}
        deadline = time.monotonic() + self.timeout
        while len(responses) < len(ids):
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TestFailure("Concurrent MCP requests timed out.")
            try:
                message = self.stdout_queue.get(timeout=remaining)
            except queue.Empty as exc:
                raise TestFailure("Concurrent MCP requests timed out.") from exc
            if "_protocol_error" in message:
                raise TestFailure(message["_protocol_error"])
            if message.get("id") in ids:
                responses[message["id"]] = message
        return [responses[request_id] for request_id in ids]

    def _timeout_message(self, method: str) -> str:
        state = self.process.poll() if self.process is not None else None
        tail = self.stderr_tail()
        suffix = f"; child exit={state}" if state is not None else ""
        if tail:
            suffix += "; stderr tail=" + " | ".join(tail[-8:])
        return f"JSON-RPC response timeout for {method}{suffix}"

    def _exit_message(self, method: str) -> str:
        tail = self.stderr_tail()
        suffix = "; stderr tail=" + " | ".join(tail[-8:]) if tail else ""
        code = self.process.returncode if self.process is not None else None
        return f"MCP child exited before responding to {method} (exit={code}){suffix}"

    def close(self) -> None:
        """Terminate only the MCP child created by this instance."""
        if self.process is None:
            return
        try:
            if self.process.stdin is not None:
                self.process.stdin.close()
        except OSError:
            pass
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                try:
                    self.process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    pass


def parse_tool_result(response: dict[str, Any]) -> ToolCall:
    if "error" in response:
        return ToolCall(False, error=response.get("error"), raw=response)
    result = response.get("result")
    if not isinstance(result, dict):
        return ToolCall(False, error=f"missing result object: {response!r}", raw=response)
    is_error = result.get("isError") is True
    payload: Any = result.get("structuredContent")
    text_value: str | None = None
    content = result.get("content")
    if isinstance(content, list):
        for block in content:
            if isinstance(block, dict) and block.get("type") == "text":
                if isinstance(block.get("text"), str):
                    text_value = block["text"]
                    try:
                        payload = json.loads(text_value)
                    except json.JSONDecodeError:
                        if payload is None:
                            payload = text_value
                    break
    if payload is None:
        payload = result
    return ToolCall(not is_error, payload, text_value, payload if is_error else None, response)


def parse_tool_map(values: list[str]) -> dict[str, str]:
    names = dict(TOOL_NAMES)
    for value in values:
        if "=" not in value:
            raise TestFailure(f"--tool-map requires logical=serverName: {value!r}")
        logical, server_name = value.split("=", 1)
        if logical not in names or not server_name.strip():
            raise TestFailure(f"unknown or empty tool map: {value!r}")
        names[logical] = server_name.strip()
    return names


def require_success(call: ToolCall, operation: str) -> Any:
    if not call.ok or call.rejected:
        raise TestFailure(f"{operation} unexpectedly failed: {call.error or call.payload!r}")
    return call.payload


def require_rejected(call: ToolCall, operation: str) -> None:
    if not call.rejected:
        raise TestFailure(f"{operation} was accepted but should be rejected: {call.payload!r}")


def find_number(value: Any, keys: tuple[str, ...]) -> float | None:
    if isinstance(value, dict):
        for key in keys:
            item = value.get(key)
            if isinstance(item, (int, float)) and not isinstance(item, bool):
                return float(item)
        for item in value.values():
            found = find_number(item, keys)
            if found is not None:
                return found
    elif isinstance(value, list):
        for item in value:
            found = find_number(item, keys)
            if found is not None:
                return found
    return None


def assert_snapshot_time(payload: Any, expected: float, operation: str) -> None:
    actual = find_number(payload, ("simulationTimeSeconds", "currentTimeSeconds", "timeSeconds"))
    if actual is None:
        raise TestFailure(f"{operation} did not return a simulation time: {payload!r}")
    if abs(actual - expected) > 1e-6:
        raise TestFailure(f"{operation} time {actual} != expected {expected}")


def path_is_within(path: Path, root: Path) -> bool:
    try:
        path.resolve(strict=False).relative_to(root.resolve())
        return True
    except ValueError:
        return False


def choose_dll(root: Path, override: Path | None) -> Path:
    if override is not None:
        return override.resolve()
    candidates = [
        root / "output" / "v4.1.0-unified" / "bin" / "MrtRouteSimulator.Mcp" / "release" / "MrtRouteSimulator.Mcp.dll",
        root / "src" / "MrtRouteSimulator.Mcp" / "bin" / "Release" / "net10.0" / "MrtRouteSimulator.Mcp.dll",
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate.resolve()
    return candidates[0].resolve()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--server-dll", type=Path, default=None)
    parser.add_argument("--desktop-pid", type=int, default=None)
    parser.add_argument("--tool-map", action="append", default=[], metavar="LOGICAL=SERVER_NAME")
    args = parser.parse_args()

    root = args.workspace_root.resolve()
    if not root.is_dir():
        raise TestFailure(f"workspace root does not exist: {root}")
    dll = choose_dll(root, args.server_dll)
    if not dll.is_file():
        raise TestFailure(f"MCP server DLL is missing; build it before running this test: {dll}")
    sample = root / "samples" / "10-小型-三站完整拓樸基準範例.mrtsim.json"
    if not sample.is_file():
        raise TestFailure(f"baseline sample is missing: {sample}")
    names = parse_tool_map(args.tool_map)
    output_parent = root / "output"
    output_parent.mkdir(parents=True, exist_ok=True)
    client: JsonRpcClient | None = None
    summary: dict[str, Any] = {"scriptVersion": SCRIPT_VERSION, "protocolVersion": PROTOCOL_VERSION}

    try:
        client = JsonRpcClient(["dotnet", str(dll), "--workspace-root", str(root)], root, RESPONSE_TIMEOUT_SECONDS)
        client.start()
        initialize = client.request(
            "initialize",
            {
                "protocolVersion": PROTOCOL_VERSION,
                "capabilities": {},
                "clientInfo": {"name": "mrt-v403-mcp-blackbox", "version": SCRIPT_VERSION},
            },
        )
        if "error" in initialize:
            raise TestFailure(f"initialize failed: {initialize['error']!r}")
        client.notify("notifications/initialized", {})

        listed = client.request("tools/list", {})
        if "error" in listed or not isinstance(listed.get("result"), dict):
            raise TestFailure(f"tools/list failed: {listed!r}")
        advertised = {
            item.get("name") for item in listed["result"].get("tools", [])
            if isinstance(item, dict) and isinstance(item.get("name"), str)
        }
        required = [names[key] for key in (
            "server_info", "project_load", "project_validate", "simulation_advance",
            "simulation_snapshot", "simulation_events", "simulation_reset", "project_save",
            "simulation_export_csv",
        )]
        missing = [name for name in required if name not in advertised]
        if missing:
            raise TestFailure(f"tools/list missing {missing!r}; advertised={sorted(advertised)!r}")
        summary["advertisedTools"] = sorted(advertised)

        def call(logical: str, arguments: dict[str, Any]) -> ToolCall:
            response = client.request("tools/call", {"name": names[logical], "arguments": arguments})
            return parse_tool_result(response)

        require_success(call("server_info", {}), "server_info")

        if args.desktop_pid is not None:
            # desktop_status is the read-only API. The current server also
            # advertises desktop_command, but its command allow-list is
            # intentionally mutating and does not include "status".
            if names["desktop_status"] in advertised:
                status_call = call("desktop_status", {"processId": args.desktop_pid})
                require_success(status_call, "desktop_status")
                summary["desktopStatusTool"] = names["desktop_status"]
            elif names["desktop_command"] in advertised:
                status_call = call("desktop_command", {
                    "processId": args.desktop_pid,
                    "command": "status",
                    "argumentsJson": "{}",
                })
                require_success(status_call, "desktop_command(status)")
                summary["desktopStatusTool"] = names["desktop_command"]
            else:
                raise TestFailure("--desktop-pid supplied but no read-only desktop status tool is advertised")

        with tempfile.TemporaryDirectory(prefix="mcp-blackbox-", dir=str(output_parent)) as temp_name:
            temp_dir = Path(temp_name).resolve()
            save_path = temp_dir / "roundtrip.mrtsim.json"
            csv_path = temp_dir / "trajectory-events.csv"
            invalid_path = temp_dir / "invalid.mrtsim.json"
            invalid_path.write_text("{ this is not valid JSON", encoding="utf-8")

            require_success(call("project_validate", {"path": str(sample)}), "project_validate(sample)")
            require_success(call("project_load", {"path": str(sample)}), "project_load(sample)")
            if names["project_get"] in advertised:
                require_success(call("project_get", {}), "project_get")
            if names["simulation_timetable"] in advertised:
                require_success(call("simulation_timetable", {}), "simulation_timetable")

            require_success(call("simulation_advance", {"targetSeconds": 10}), "simulation_advance(10)")
            snapshot_10 = require_success(call("simulation_snapshot", {}), "simulation_snapshot(10)")
            assert_snapshot_time(snapshot_10, 10, "simulation_snapshot(10)")
            if snapshot_10["snapshot"]["newEvents"] != []:
                raise TestFailure("state snapshots must expose no event delta")
            repeated = require_success(call("simulation_snapshot", {}), "repeated snapshot")
            if repeated != snapshot_10:
                raise TestFailure("state snapshots must be repeatable")
            events = require_success(call("simulation_events", {"offset": 0, "limit": 100}), "simulation_events")
            if not isinstance(events, (dict, list)):
                raise TestFailure(f"simulation_events returned unexpected payload: {events!r}")

            if events.get("total", 0) < 1 or not events.get("items"):
                raise TestFailure("event pagination must retain initial departure history")

            # A failed load must leave the active session at t=10.
            require_rejected(call("project_load", {"path": str(invalid_path)}), "project_load(invalid JSON)")
            after_invalid = require_success(call("simulation_snapshot", {}), "snapshot(after invalid load)")
            assert_snapshot_time(after_invalid, 10, "snapshot(after invalid load)")

            require_success(call("simulation_reset", {}), "simulation_reset")
            snapshot_0 = require_success(call("simulation_snapshot", {}), "simulation_snapshot(reset)")
            assert_snapshot_time(snapshot_0, 0, "simulation_snapshot(reset)")
            require_rejected(call("simulation_advance", {"targetSeconds": -1}), "simulation_advance(-1)")

            # Different SDK tool invocations must serialize access to the shared mutable world.
            for iteration in range(3):
                concurrent = client.request_many([
                    ("tools/call", {"name": names["simulation_advance"], "arguments": {"targetSeconds": 20}}),
                    ("tools/call", {"name": names["simulation_reset"], "arguments": {}}),
                ])
                advanced = require_success(parse_tool_result(concurrent[0]), "concurrent advance")
                reset = require_success(parse_tool_result(concurrent[1]), "concurrent reset")
                assert_snapshot_time(advanced, 20, "concurrent advance")
                assert_snapshot_time(reset, 0, "concurrent reset")
                require_success(call("simulation_reset", {}), "reset after concurrent test")
            summary["concurrentAdvanceResetRounds"] = 3

            outside = Path(tempfile.gettempdir()).resolve() / "mrt-v403-mcp-outside-workspace.json"
            if path_is_within(outside, root):
                outside = root.parent / "mrt-v403-mcp-outside-workspace.json"
            require_rejected(call("project_validate", {"path": str(outside)}), "project_validate(outside workspace)")

            require_success(call("project_save", {"path": str(save_path), "overwrite": False}), "project_save(first)")
            require_rejected(call("project_save", {"path": str(save_path), "overwrite": False}), "project_save(overwrite=false)")
            require_success(call("simulation_export_csv", {"path": str(csv_path), "overwrite": False}), "simulation_export_csv(first)")
            if not csv_path.is_file():
                raise TestFailure(f"CSV call succeeded but created no file: {csv_path}")
            require_rejected(call("simulation_export_csv", {"path": str(csv_path), "overwrite": False}), "simulation_export_csv(overwrite=false)")

            summary.update({
                "sample": str(sample),
                "snapshotAfterAdvanceSeconds": 10,
                "snapshotAfterResetSeconds": 0,
                "eventPayloadType": type(events).__name__,
                "csvBytes": csv_path.stat().st_size,
                "scopedOutputDirectory": str(temp_dir),
                "passed": True,
            })
        print(json.dumps(summary, ensure_ascii=False, indent=2))
        return 0
    except Exception as exc:
        summary["passed"] = False
        summary["error"] = str(exc)
        if client is not None:
            summary["stderrTail"] = client.stderr_tail()[-8:]
        print(json.dumps(summary, ensure_ascii=False, indent=2), file=sys.stderr)
        return 1
    finally:
        if client is not None:
            client.close()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except TestFailure as exc:
        print(json.dumps({"passed": False, "error": str(exc)}, ensure_ascii=False, indent=2), file=sys.stderr)
        raise SystemExit(1)
