# V4.1.1 多營運路線與逐站發車

本次以 origin/main `159a70779e75f38fb95992da342b3d02e4bc0c87` 的新版介面為基底。保留新版主題、卡片、導覽與圖表，新增功能沿用 Schema 8 及同一 SimulationWorld，沒有另建模擬資料源。

## 操作入口

專案工作區 → 營運路線網路：設定 1～8 條路線、每線單／雙線、父線與接軌公里里程。「本線區段長度」與「本線站數」不包含父線共用幹線。

班表與接續 → 手動班表與接續：選取車次後按「逐站時刻…」，以 HH:mm:ss 設定表定發車時刻及最少停站秒數。

## 資料與控制

OperatingLines 描述路線名稱、單線／雙線、父線、父線接軌里程及該線長度。OperatingNetworkBuilder 依父線順序建置 topology，將接軌點拆成實體 node／edge，使用明確 A/B port 與 directed connection。支線 ServiceRoute 重用祖先起點至接軌點的實體幹線，分岔後使用自己的股道。營運路線與股道數是不同設定。

各班次的 ServiceRouteId 指定行駛路徑；未指定時沿用專案明確的方向別 binding。事件、快照、軌跡、時刻表與區間結果保留 ServiceRouteId／OperatingLineId。投影公里數僅供顯示，物理位置仍為 TrackEdgeId／OffsetMeters。

每車次 StationTimetableEntries 的 PlannedDepartureTime 採班表時鐘；MinimumDwellSeconds 是該站最少停站秒數。中途站的最早離站時刻為 max(實際到站秒＋設定停站秒數，表定發車相對秒)，並且必須滿足安全、進路、待避及資源條件。逐站秒數覆寫該次的停站模式／站預設，未設定的站維持原規則。零秒仍保留一個固定 tick 發車事件。

實際停站時間由同一 world 的 Departure－Arrival 事件相減，包含等時刻及控制延長；未離站保持空值。跨站不適用。起站沒有到站事件，逐站停站設定必須為 0 且與車次發車時刻一致；終點離站由反向車次起站設定，不能另加一般發車欄位。

## 控制範圍與限制

單線使用整線 Block 資源做保守互斥預約，對向車等待前車車尾清空。接軌沿用 Switch 共用資源。這不是自動分區閉塞或會車班表最佳化；需要精細站間會車時，仍須使用既有實體待避設施與手動班表設計，並驗證進路。

多線建置是重新產生草稿的站線、營運目錄與派車。先另存原專案，檢查草稿再套用；取消不替換主畫面。建置產生的站名、等距站距、車型與時刻均為 synthetic 示範，不代表實際鐵路資料。之後可使用既有拓樸頁修改車站、股道及設施。

## 驗證與使用

自動化及新版 WPF 操作驗證結果以 QA_REPORT.md 的 V4.1.1 節為準。離屏 WPF 渲染與 named pipe 操作不等於原生桌面 DPI、滑鼠手感或 compositor 驗收。此次沒有提交、推送或發布。

建置：

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release -p:ArtifactsPath=D:/AI/codex/mrt-route-simulator/output/v411-final --configfile .\src\MrtRouteSimulator.Mcp\NuGet.Config
```

MCP 直接使用同一版本產物與 Schema 8 JSON，不需 Computer Use 設定模型：讀取 project_get、修改完整 JSON 後 project_replace、推進 simulation_advance、檢視 simulation_timetable。載入或替換失敗保留現有 session。
