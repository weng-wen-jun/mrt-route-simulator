#!/usr/bin/env python3
"""Verify the registered MCP -> new WPF process -> named pipe chain.
Only the desktop process created by this test is closed, using WM_CLOSE.
"""
from __future__ import annotations
import argparse
import ctypes
from ctypes import wintypes
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

spec = importlib.util.spec_from_file_location("mrt_mcp_test", Path(__file__).with_name("test-mcp.py"))
mcp = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = mcp
spec.loader.exec_module(mcp)

def close_owned_desktop(pid: int, process_handle: int) -> None:
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    user = ctypes.WinDLL("user32", use_last_error=True)
    kernel.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    exit_code = wintypes.DWORD()
    if not kernel.GetExitCodeProcess(process_handle, ctypes.byref(exit_code)) or exit_code.value != 259:
        return
    def callback(hwnd, _):
        owner = wintypes.DWORD()
        user.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
        if owner.value == pid:
            user.PostMessageW(hwnd, 0x0010, 0, 0)  # WM_CLOSE on our test process only.
        return True
    cb = callback_type(callback)
    user.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    user.EnumWindows(cb, 0)
    if kernel.WaitForSingleObject(process_handle, 10000) != 0:
        raise RuntimeError(f"Test desktop PID {pid} did not close normally; left running for inspection.")

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace-root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    if os.name != "nt":
        raise RuntimeError("Windows is required.")
    root = args.workspace_root.resolve()
    environment = dict(os.environ, CODEX_HOME=str(Path.home() / ".codex"))
    registration = subprocess.run([shutil.which("codex"), "mcp", "get", "mrt-route-simulator", "--json"],
        capture_output=True, text=True, encoding="utf-8", env=environment, cwd=root, check=True)
    registered = json.loads(registration.stdout)
    transport = registered["transport"]
    if not registered["enabled"] or transport["type"] != "stdio":
        raise RuntimeError("Registered stdio server is not enabled.")
    command = [transport["command"], *transport["args"]]
    client = mcp.JsonRpcClient(command, root, 60)
    pid = None
    handle = None
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    summary = {"registration": "mrt-route-simulator", "passed": False, "closedNormally": False}
    try:
        client.start()
        initialized = client.request("initialize", {
            "protocolVersion": mcp.PROTOCOL_VERSION, "capabilities": {},
            "clientInfo": {"name": "mrt-desktop-e2e", "version": "1.0"}})
        if "error" in initialized:
            raise RuntimeError(str(initialized["error"]))
        client.notify("notifications/initialized", {})
        def tool(name, arguments):
            result = mcp.parse_tool_result(client.request("tools/call", {"name": name, "arguments": arguments}))
            return mcp.require_success(result, name)
        def desktop(command, arguments=None):
            return tool("desktop_command", {"processId": pid, "command": command,
                "argumentsJson": json.dumps(arguments or {}, ensure_ascii=False)})
        launched = tool("desktop_launch", {})
        pid = launched["processId"]
        handle = kernel.OpenProcess(0x100000 | 0x1000, False, pid)  # synchronize + query-limited
        if not handle:
            raise RuntimeError("Cannot open test desktop process handle.")
        time.sleep(1)
        status = tool("desktop_status", {"processId": pid})
        assert status["processId"] == pid
        assert Path(status["workspaceRoot"]).resolve() == root
        original_zoom = status["routeZoom"]
        desktop("load", {"path": "samples/10-小型-三站完整拓樸基準範例.mrtsim.json"})
        advanced = desktop("advance", {"targetSeconds": 10})
        assert advanced["simulationTimeSeconds"] == 10
        desktop("select_page", {"page": "diagram"})
        zoomed = desktop("zoom", {"route": 1.25, "horizontal": 1.5, "vertical": 1.25})
        assert zoomed["routeZoom"] == 1.25 and zoomed["diagramHorizontalZoom"] == 1.5
        sizes = {}
        with tempfile.TemporaryDirectory(prefix="mcp-desktop-e2e-", dir=root / "output") as temp:
            for fmt in ("csv", "png", "pdf"):
                path = Path(temp) / ("diagram." + fmt)
                desktop("export", {"path": str(path), "format": fmt})
                assert path.is_file() and path.stat().st_size > 0
                sizes[fmt] = path.stat().st_size
        assert desktop("play", {"rate": 10})["isPlaying"]
        assert not desktop("pause")["isPlaying"]
        events = desktop("events", {"offset": 0, "limit": 5})
        assert len(events["items"]) <= 5
        timetable = desktop("timetable", {"limit": 5})
        assert len(timetable["items"]) <= 5
        reset = desktop("reset")
        assert reset["simulationTimeSeconds"] == 0
        desktop("zoom", {"route": original_zoom})
        summary.update(processId=pid, simulationTimeAfterAdvance=10, snapshotAfterReset=0,
            outputBytes=sizes, desktopCommands="load/advance/select_page/zoom/export/play/pause/events/timetable/reset",
            passed=True)
    finally:
        try:
            if pid is not None and handle:
                close_owned_desktop(pid, handle)
                summary["closedNormally"] = True
        finally:
            if handle:
                kernel.CloseHandle(handle)
            client.close()
            report = root / "output" / "mcp-desktop-smoke.json"
            report.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
