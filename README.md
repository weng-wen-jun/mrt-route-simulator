# MRT 路線進出站時間模擬器 V1.0

這是一套完全離線的 Windows 桌面軟體，用來模擬單一直線捷運路線的列車進出站時間。程式包含獨立物理 Engine、固定 0.1 秒 Simulation Tick、WPF 操作介面、路線動畫、速度曲線、多列車時刻表及自動化測試。

## 直接使用

1. 開啟 `release/MRT 路線進出站時間模擬器 V1.0`。
2. 雙擊 `MRT路線進出站時間模擬器.exe`。
3. 第一次可直接使用六站示範資料，按「計算並建立模擬」。
4. 按「播放」查看列車運行；可切換「進出站時刻表」及「區間物理明細」。

本機需要 Microsoft .NET 10 Desktop Runtime。本專案不使用網路、帳號、資料庫或第三方套件。

## 使用方式

### 1. 編輯路線

- 第一站的「前站 km」必須為 `0`。
- 第二站以後輸入「與前一站距離」，程式會在 Engine 內累加為 `position`。
- 每站可指定停站秒數；空白時使用預設停站時間。
- 可新增、刪除、上移、下移車站，至少保留 2 站。

### 2. 設定列車

- UI 的最高速度使用 km/h；傳入 Engine 前會轉成 m/s。
- 加速度、減速度使用 m/s²。
- 起點及終點折返時間使用分鐘；傳入 Engine 前會轉成秒。

### 3. 設定多列車

- 指定班距留空時：`理論班距 = 循環時間 / 列車數量`。
- 若輸入班距，則以使用者指定值發車。
- 播放倍率只影響畫面速度，不改變模擬時間及物理結果。

### 4. 解讀結果

- 單程運行時間：起點出發至終點抵達，包含中間站停站，不含起點與終點停站。
- 完整循環時間：下行單程 + 終點折返 + 上行單程 + 起點折返。
- 理論班距：不包含號誌、閉塞、追蹤限制及折返緩衝的營運限制。
- 時刻表的時間只在 UI 輸出階段格式化；Engine 全程使用秒。

## 開發與驗證

在專案根目錄執行：

```powershell
dotnet restore .\MrtRouteSimulator.slnx --configfile .\NuGet.Config
dotnet build .\MrtRouteSimulator.slnx --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj --no-build --no-restore
```

主要檔案：

- `src/MrtRouteSimulator.Engine`：完全不依賴 UI 的核心 Engine。
- `src/MrtRouteSimulator.App`：WPF 桌面操作介面。
- `tests/MrtRouteSimulator.Tests`：無外部測試框架的自動化測試執行器。
- `MODEL_SPEC.md`：完整資料結構、公式、API、狀態與邊界定義。
- `QA_REPORT.md`：建置、測試與 Windows UI 驗收紀錄。

## V1.0 限制

只支援單一路線、直線、固定站點、同性能列車及理論均勻班距。不包含號誌、閉塞、列車追蹤、超車、故障、臨時限速、坡度、曲線、不同車種、地圖及乘客上下車時間模型。
