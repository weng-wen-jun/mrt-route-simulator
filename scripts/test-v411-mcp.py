#!/usr/bin/env python3
"""Verify multi-route station clocks through the real MCP stdio transport."""
import argparse, importlib.util, json, sys
from pathlib import Path

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--server-dll", type=Path, required=True)
    args = parser.parse_args()
    root = args.workspace_root.resolve()
    spec = importlib.util.spec_from_file_location("mrt_mcp_test", root / "scripts/test-mcp.py")
    mcp = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = mcp
    spec.loader.exec_module(mcp)
    sample = root / "samples/15-小型-主支線逐站班表範例.mrtsim.json"
    project = json.loads(sample.read_text(encoding="utf-8-sig"))
    plans = project["dispatch"]["manualTimetableRows"]
    client = mcp.JsonRpcClient(["dotnet", str(args.server_dll.resolve()), "--workspace-root", str(root)], root, 60)
    try:
        client.start()
        initialized = client.request("initialize", {"protocolVersion": mcp.PROTOCOL_VERSION, "capabilities": {},
            "clientInfo": {"name": "mrt-v411-regression", "version": "1.0"}})
        assert "error" not in initialized
        client.notify("notifications/initialized", {})
        def call(name, args):
            return mcp.require_success(mcp.parse_tool_result(client.request("tools/call", {"name": name, "arguments": args})), name)
        call("project_load", {"path": str(sample)})
        saved = call("project_get", {})
        assert saved["operatingLines"] == project["operatingLines"]
        assert saved["dispatch"]["manualTimetableRows"] == plans
        state = call("simulation_snapshot", {})
        for target in range(60, 1801, 60):
            state = call("simulation_advance", {"targetSeconds": target})
            if state["isComplete"]:
                break
        assert state["isComplete"], "sample did not complete in bounded time"
        rows = call("simulation_timetable", {"offset": 0, "limit": 500})["items"]
        origin_clock = plans[0]["plannedDepartureTimeSeconds"]
        checked = 0
        for plan in plans:
            route_rows = [row for row in rows if row["serviceRunId"] == plan["serviceRunId"]]
            assert route_rows and all(row["serviceRouteId"] == plan["serviceRouteId"] for row in route_rows)
            for entry in plan.get("stationTimetableEntries") or []:
                row = next(row for row in route_rows if row["stationId"] == entry["stationId"])
                if row["actualArrivalTimeSeconds"] is None:
                    continue
                assert row["plannedDwellSeconds"] == entry["minimumDwellSeconds"]
                assert row["actualDwellSeconds"] + 0.11 >= entry["minimumDwellSeconds"]
                schedule = (entry["plannedDepartureTimeSeconds"] - origin_clock) % 86400
                assert row["actualDepartureTimeSeconds"] + 0.11 >= schedule
                assert abs(row["actualDwellSeconds"] - (row["actualDepartureTimeSeconds"] - row["actualArrivalTimeSeconds"])) < 0.001
                checked += 1
        assert checked >= 2
        call("simulation_reset", {})
        mcp.assert_snapshot_time(call("simulation_snapshot", {}), 0, "V4.1.1 reset")
        print(json.dumps({"passed": True, "runCount": len(plans), "timetableRows": len(rows), "stationSchedulesChecked": checked}, ensure_ascii=False, indent=2))
    finally:
        client.close()

if __name__ == "__main__":
    main()
