# 更新包與資料更新發行流程

本專案把更新拆成兩個獨立通道。程式更新目前使用完整受管理套件，尚未加入差分 OTA：

- **Application**：程式檔、MCP、runtime 與 updater。GitHub Release tag 使用 `vX.Y.Z`，資產為 `mrt-application-update-vX.Y.Z.zip` 與固定名稱 `mrt-application-update.json`。
- **Data**：Schema 8 範例資料。GitHub Release tag 使用 `data-vX.Y.Z`，資產為 `mrt-data-update-vX.Y.Z.zip` 與固定名稱 `mrt-data-update.json`。

用 `scripts/Build-UpdatePackages.ps1` 產製。它讀取已建置的 `AppDirectory` 與遞迴 `SamplesDirectory`，只在全新的 `OutputDirectory` 建立 staging、ZIP、JSON 與 `portable` 子目錄；來源目錄不會被改寫，已存在的輸出目錄會直接拒絕。

```powershell
pwsh .\scripts\Build-UpdatePackages.ps1 `
  -AppDirectory .\artifacts\app `
  -OutputDirectory .\artifacts\update-v4.1.1 `
  -AppVersion 4.1.1 `
  -DataVersion 4.1.1 `
  -SamplesDirectory .\samples `
  -SchemaVersion 8
```

產物如下：

```text
OutputDirectory/
├─ mrt-application-update-vX.Y.Z.zip
├─ mrt-application-update.json
├─ mrt-data-update-vX.Y.Z.zip
├─ mrt-data-update.json
└─ portable/
   ├─ MRT路線進出站時間模擬器.exe
   ├─ updater/
   ├─ mrt-installed-files.json
   └─ samples/
```

application ZIP 只包含 `UpdatePathPolicy.IsAllowedApplicationPath` 對應的 allowlist：根目錄的程式檔、`runtimes/`、`mcp/`、`updater/` 下的允許副檔名，以及根目錄 `README.md`／`LICENSE.txt`。它必須包含主程式與 `updater/MrtRouteSimulator.Updater.dll`。receipt 的 `files` 只列 application allowlist，**不包含 receipt 自己**。

data ZIP 只包含 `samples/` 下遞迴找到的 `.mrtsim.json` 與 `README.md`；其他來源檔案不會進入 data update manifest。portable 會保留 SamplesDirectory 的整個安全檔案樹，供完整可更新安裝包使用。所有清單路徑使用 `/`，檔案路徑、reparse point、重複項目、父檔案/子路徑衝突都會檢查。

兩份 manifest 對應 `UpdateModels.cs` 的 camelCase 欄位與 string enum：`formatVersion`、`product`、`kind`、`version`、`minimumAppVersion`、`schemaVersion`、`packageFileName`、`packageSha256`、`packageSize`、`files[]`。每個 file record 都記錄實際未壓縮檔案的 `path`、小寫 SHA-256 與 `size`；`packageSha256`／`packageSize` 則在 ZIP 完成後重新計算。application 的 `minimumAppVersion` 目前產製為 `0.0.0`，讓有合法 receipt 的舊版可以升級；data 的最低程式版本是同次輸入的 `AppVersion`，避免 Schema 8 資料送給過舊程式。

## 軟體內操作

開啟「檔案 → 更新中心」可分別檢查程式與範例資料。啟動時檢查預設關閉；開啟後只通知，不自動下載或安裝。

資料更新會下載及驗證全部檔案，逐份使用現行 Engine 反序列化並建立 topology runtime，成功才切換資料庫；目前工作區與使用者存檔不變。可在同視窗選取維護範例載入，再另存自己的專案。

程式更新先下載及驗證，再由使用者按「安裝並重新啟動」。確認後先將目前專案另存復原副本；副本寫入失敗就停止更新。更新器在主程式正常結束後才替換檔案，不要求管理員權限，也不強制終止程序。重新開啟後可在更新中心按「載入最近的更新前專案副本」，再另存到自己的位置。

更新狀態集中於 %LocalAppData%\MrtRouteSimulator\Updates：

- Downloads：已驗證套件暫存。
- Data/versions/版本/payload：各版維護範例；Data/active.json 為目前版本指標。
- Recovery：更新前專案副本。
- Jobs/工作編號：獨立 runner、工作單、更新結果、日誌及備份 journal。

斷電後若留下 mrt-update.pending.json，更新器會拒絕重複更新；該檔記錄備份位置。關閉主程式後可使用工作目錄的 runner 執行 --recover 備份目錄，確認復原結果後再手動開啟程式。它不會覆蓋交易後另有變更的檔案。

目前公開 Release 若只有舊 ZIP 而沒有上述 manifest，更新中心會顯示尚無可驗證更新，不會自行猜測下載或覆寫。此實作與本地套件驗證不代表已對外發布。

## 發布與客戶端校驗

發布前應用清單與 ZIP 一起上傳同一個 GitHub Release。資產名稱、tag、manifest `packageFileName`、GitHub asset `size` 與 `digest` 必須完全一致；GitHub API 的 digest 形式是 `sha256:<64 位小寫十六進位>`。客戶端 `UpdateRepositoryClient` 只接受固定 repository、穩定 tag、固定資產路徑及 SHA-256 digest，下載後由 `PackageStager` 再驗證 package hash、ZIP 項目、每個檔案大小/hash、路徑與 Schema。

建立 release 前至少核對：

1. application 使用 `vX.Y.Z`，data 使用 `data-vX.Y.Z`，兩者都是三段式穩定版本。
2. `mrt-application-update.json`／`mrt-data-update.json` 的 `packageFileName` 與 ZIP 資產名稱一致。
3. manifest 的 package hash/size 與 GitHub Release API asset digest/size 一致，所有 hash 使用小寫。
4. data manifest 的 `schemaVersion` 與程式的 `TopologyProjectFormat.CurrentSchemaVersion` 一致。
5. 下載後解壓只得到 manifest 列出的檔案；不接受 ZIP Slip、reparse/symlink、大小寫重複、壓縮炸彈或清單外檔案。

## 舊 portable 與失敗復原

可更新的軟體安裝必須同時有 `mrt-installed-files.json` 與 `updater/MrtRouteSimulator.Updater.dll`。舊發布包若沒有 receipt，客戶端不會猜測既有檔案所有權，也不會直接覆寫；請先以完整 `portable` 包替換／重新安裝到新的乾淨目錄，之後才使用 application 完整套件更新。

程式更新由獨立 updater 在主程式退出後執行，先寫 durable journal、備份受管理檔案，再逐檔驗證與 atomic replacement；失敗時使用 backup rollback。若電源中斷或 helper 在交易期間終止，保留備份與 journal，應使用 updater 的 recovery 流程，不要在檔案狀態未知時重試。資料更新使用版本目錄與 active pointer：先在新版本目錄驗證全部資料並建立 runtime，最後才切換 pointer；失敗時舊版本仍是 active。

這個 script 不下載、不呼叫 GitHub、不 publish、不簽署、不 commit，也不會執行應用程式或 updater。它只產製本地發行材料；真正 release 前仍需在乾淨工作目錄完成 build、外部驗證、資產上傳與 Release gate。

## Maa 借鑑與授權界線

更新流程的設計參考官方 MaaAssistantArknights 穩定 WPF release [`v6.18.0`](https://github.com/MaaAssistantArknights/MaaAssistantArknights/releases/tag/v6.18.0)，尤其是 [`PendingUpdateApplier.cs`](https://github.com/MaaAssistantArknights/MaaAssistantArknights/blob/23feeddc538de4e10646b692b0bb065efe77ccb3/src/MaaWpfGui/Services/PendingUpdateApplier.cs)、[`MaaUpdater/main.cpp`](https://github.com/MaaAssistantArknights/MaaAssistantArknights/blob/23feeddc538de4e10646b692b0bb065efe77ccb3/src/MaaUpdater/main.cpp) 的 pending plan、獨立自我替換、備份、重啟與失敗恢復概念；目前 `dev-v2` 的相同責任路徑仍可見於 [`63e8ab6`](https://github.com/MaaAssistantArknights/MaaAssistantArknights/commit/63e8ab628c955155ee0de974f6b9b3ff50e15dad)。

Maa repository 與 WPF/updater source 採 AGPL-3.0-only（見其 [`LICENSE`](https://github.com/MaaAssistantArknights/MaaAssistantArknights/blob/23feeddc538de4e10646b692b0bb065efe77ccb3/LICENSE)／[`LICENSE.spdx`](https://github.com/MaaAssistantArknights/MaaAssistantArknights/blob/23feeddc538de4e10646b692b0bb065efe77ccb3/LICENSE.spdx)）。本專案是 MIT；此處只採用獨立設計概念與介面契約，沒有複製 Maa 的 AGPL 程式碼、類別、字串或實作。未來若引入任何第三方更新器程式，必須另做授權相容性審核。

已知限制：application manifest 尚未攜帶檔案簽章；GitHub digest 與 HTTPS/固定 repository 驗證是目前信任邊界。Build-UpdatePackages可接受framework-dependent或self-contained來源；正式Windows完整包由Build-Release產製並附帶.NET runtime與對應第三方授權；本 script 不替建置、測試、原生 Windows 安裝驗收或 release gate 背書。
