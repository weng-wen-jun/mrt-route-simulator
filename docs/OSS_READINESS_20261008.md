# OSS / Codex for Open Source 申請前整理報告

日期：2026-10-08（Asia/Taipei）。來源基準：`217c803`，`codex/nearest-leader-index-prototype`，來源版本 4.1.0。此為文件／授權／公開材料邊界整理，不是 simulator 功能更新、法律意見或完整資安稽核。

> 本報告的待提交／遠端未更新狀態為初次整理時的快照。後續使用者已授權本地提交與正常推送至 `origin/main`、保留歷史、啟用私人漏洞回報，不含 tag／Release。私人漏洞回報已於 2026-10-08 啟用並由 API 讀回 `enabled=true`；新增 `SECURITY.md` 及 README 入口。提交／推送完成後以遠端 commit 與實際內容核對，不把下文歷史快照當作最新狀態。

## A. Changes Made

- `LICENSE`：標準 MIT 原文，Copyright (c) 2026 weng-wen-jun。Git author 與 repository owner 均為此 handle，不猜測真實姓名。範圍僅涵蓋作者有權授權的專案內容；第三方套件與材料維持原授權。標準文字來源：[Open Source Initiative](https://opensource.org/license/mit)。
- `.gitignore`：加入 `/外部檔案參考/`，保留既有 build／artifact 排除；`.gitattributes` 加入該目錄 `export-ignore`，防止未來 source archive 誤納入。
- `README.md`：英文主標題、英中摘要、用途、概念模擬安全限制、精簡能力、英文 Quick Start、實測 build/test 指令、少量真實 badges、MIT／第三方材料界線。原中文操作與技術內容保留；明確區分本地未發布 V4.1.0 與正式 V4.0.3。
- 後續維護者澄清（2026-10-08）：大型機場線為抽象規劃，非實際線路里程；已同步 README 與 samples manifest。未來取得政府公開可信資料後，確認使用條件並註明來源、資料日期與更新範圍再更新。本次澄清只改文件，不改案例數值或 runtime。
- `CONTRIBUTING.md`：英文 onboarding、issue／PR 流程、保密／資料權限、非安全認證限制與 AI-assisted workflow；保留既有架構、固定子步進與 regression 要求，修正完整方案的 restore 設定。
- `MODEL_SPEC.md`：根目錄缺失已導致 AGENTS／HANDOFF／CONTRIBUTING 斷鏈；从本專案 Git `558e06b` 的 blob `bc1717bc385909d14e40b2f5c31617821c94e01c` 恢復原有正文，另加歷史版本說明。不是搬入第三方文件，也未修改公式；舊 V4.0.2 正文不是 V4.1.0 全量規格。
- `QA_REPORT.md` 與原遷移紀錄：補本輪結果與參考檔公開規則的後續變更，不改寫歷史驗收為 PASS。
- GitHub About：`Offline Windows MRT / railway operations and topology simulation tool`。
- GitHub Topics：`railway-simulation`, `metro`, `transit`, `transportation`, `railway`, `simulation`, `csharp`, `wpf`, `train-simulation`, `operations-research`。管理權已確認，設定後已讀回驗證。未改存取權限。
- 初次整理未機械式新增 SECURITY.md、CODE_OF_CONDUCT.md 或 CITATION.cff。後續經使用者授權，已啟用並驗證 GitHub 私人漏洞回報，補 SECURITY.md；不提供私人 email 或虛構回覆時限。其餘治理與引用資料沒有本輪必要的新需求。

## B. External Reference Exclusion

- 原 index／HEAD：16 份 tracked Markdown；新交辦另有 1 份 untracked，本機總共 17 份。原 `.gitignore` 無 reference 規則。
- 已執行 `git rm -r --cached -- "外部檔案參考"`；只解除 index 追蹤，沒有刪除或修改本機參考內容。
- 現在 `git ls-files 外部檔案參考` 為 0；本機仍 17 份，普通 Git add 會被 `.gitignore` 排除。強制 add 可繞過忽略規則，不能把它當成資安控制。
- 已有獨立備份 `D:/AI/codex/mrt-oss-reference-backup-20261008.zip`，包含 17 份。建立時與完成前逐檔驗證 SHA256 相同。ZIP SHA256：`45A1AB0894DAF63599ECE5CB7B743D41A5D318D486D6194724A2F5CB3D3F3143`。先前整合備份與 history.bundle 仍保留。

```text
Working copy: retained (17 files, independent verified backup)
Git index: excluded (0 files; removals staged)
Local HEAD: still contains previous files until an approved commit
GitHub HEAD: still contains external references until an approved commit/push
Future ordinary tracking: excluded by .gitignore
Git history / old tags / releases: unchanged
```

### 維護者澄清與歷史文件標示差異

維護者於 2026-10-08 明確說明大型機場線為抽象規劃、非實際線路里程，因此不再把現行 sample 的里程當作「實際線路來源權限未確認」的阻擋項。先前來源文件的措辭與此說明有差異，以下保留歷史檢查紀錄，不將歷史標籤當成正式來源證據。這項澄清不是所有歷史第三方材料權利的獨立法律驗證。限定歷史與目前材料檢查沒有找到高可信 credential pattern，不代表所有歷史安全。

| Path / material | Commit evidence | 類型及需確認事項 |
|---|---|---|
| 外部檔案參考/大型機場線_完整站序來源資料.md | `9e2b1ac` | 歷史文件使用 source-backed 措辭，與維護者此次抽象規劃說明不同；保留本機原件，不據此認定里程為實際資料。此本地整合 commit 本輪未推送。 |
| 外部檔案參考/CODEX_TASK_Taichung_full_station_chain_source_data.md | `2a68bee`, `89d9818`, `9e2b1ac` | 歷史有正式規劃／站心里程措辭，僅記錄標示差異，不認定已確認侵權或敏感資料。若日後引用實際政府資料，另確認來源與使用條件。 |
| samples/13-中型-六站主要站示範範例.mrtsim.json、samples/14-大型-二十八站完整營運範例.mrtsim.json；SyntheticLongRouteScenarioBuilder | 現行 `217c803`；樣本沿革 `89d9818` | 維護者已確認為抽象規劃；manifest 已明確標示非實際線路里程。數值與模型不變，保留 regression 對照，不宣稱正式資料校準。 |

目前依使用者要求排除參考目錄、保留本機與備份，不因歷史措辭差異自行清除歷史。若後續找到具體第三方材料問題，再另行審查並取得清理授權。本輪不 force push、不 rewrite history、不改 sample 值或移除正式 release。

已確認歷史含該目錄，例如 `ea3dedc`、`5316a30`、`2a68bee`、`89d9818`、`9e2b1ac`；遠端預設 `main` 現行內容亦有該目錄。移除最新版不會讓旧 commit／tags／fork／clone 消失。未宣稱完整 repository 全歷史或所有第三方原始碼 provenance 已審完。

## C. OSS Readiness Assessment

| 項目 | 狀態 | 證據／界線 |
|---|---|---|
| Public repository | PASS | GitHub API 回傳 public；[repository](https://github.com/weng-wen-jun/mrt-route-simulator)。 |
| Open-source license | WARNING | 本地標準 MIT 已建立；遠端 API license 仍為 null，需核准提交／推送後重新確認 GitHub 識別。不能稱遠端已 MIT 授權。 |
| Maintainer ownership | PASS | owner／Git author handle 一致，登入 CLI 對此 repo admin／maintain／push 權限為 true；不等同全部第三方材料的法律權利驗證。 |
| README / Quick Start | PASS（local） | 英文入口保留中文技術內容；標示 UI 主要為繁體中文，sample 與 executable 路徑已查驗。遠端待更新。 |
| Build instructions | PASS | MCP NuGet.Config 還原、標準 Release build 與兩 runners 實際執行；root NuGet.Config 為離線核心，不誤列 fresh full-solution restore。 |
| Tests | PASS | Engine 198/198、完整 default WPF exit 0；未新增假的 CI badge，不等同原生 DPI／compositor gate。 |
| Releases | PASS | [V4.0.3](https://github.com/weng-wen-jun/mrt-route-simulator/releases/tag/v4.0.3)，2026-09-28，Windows ZIP 與 SHA256SUMS；V4.1.0 未發布。未驗證既有 ZIP／歷史 source archive 是否完全無外部資料。 |
| Contribution guide | PASS（local） | build/tests、架構、issues、PR、regression、敏感資料与 AI 驗證流程。 |
| External reference exclusion | WARNING | index 0、本機保留；遠端 HEAD／歷史待核准處理。 |
| Third-party materials | WARNING（審查範圍限制） | HEAD tracked PDF／圖片／archive／binary 為 0；限定檢查未見抄入第三方 code 提示。大型機場線里程已由維護者確認為抽象規劃；第三方套件、URCS 相容性基準與其他歷史材料仍非本輪完整法律或 provenance 審核。 |
| Security documentation | PASS（後續授權更新） | GitHub private vulnerability reporting=true，API 已讀回；SECURITY.md 使用實際私人回報入口，simulation accuracy／營運假設不一概視為 cybersecurity 漏洞。 |
| Screenshots | MANUAL ACTION | 沒有可直接納入的 tracked 真實畫面素材；不捏造 UI。建議提供主路線圖及運行圖／editor 各一張並去除私人路徑。 |
| GitHub Topics | PASS | 10 個指定 topics 已設定並讀回。 |
| Repository description | PASS | 英文 About 已設定並讀回。 |
| Code of Conduct / CITATION.cff | WARNING（非必要） | 尚未建立；目前沒有新增治理／學術引用資料的明確需求，不作為基本 OSS 必要 gate。 |

## D. Manual GitHub Actions

1. 大型機場線 sample 的資料性質已由維護者澄清，無需以實際線路資料要求其來源證明。保留歷史文件標示差異；日後若引用政府公開可信資料，確認使用條件並記錄來源與日期。任何歷史清理仍須另行授權。
2. 核准本輪聚焦文件／授權／reference exclusion 提交與後續推送，再核對 GitHub LICENSE=MIT、預設 branch 最新樹不含參考目錄。建議 commit：`docs: prepare repository for open-source distribution`；本輪不自行 commit。
3. 若未來發布 release，明確檢查打包清單排除本機參考目錄，不打包整個本機 workspace；既有 release ZIP／source archive 狀態尚未重新盤點，不宣稱已清理。
4. 提供至少兩張真實程式畫面，確認展示資料可公開，檢查標題／狀態列的本機路徑；可另設定 Social Preview。
5. 已依後續授權啟用 GitHub 私人漏洞回報並新增 SECURITY.md，無需提供私人 email。
6. 維護者已確認目前由本人用於列車運行研究。可用的申請敘述為：「我目前使用 MRT Route Simulator 進行個人列車運行研究。」／“I currently use MRT Route Simulator for my own research into train operations.” 不宣稱其他使用者、機構採用或正式營運用途；先前 API 顯示 1 star／0 forks，不能寫成廣泛採用。申請由維護者提交，不自動代填、同意條款或傳送機密資料。

## E. Verification Result

```text
Restore: exit 0 (MCP NuGet.Config, actual standard README command)
Build: exit 0 (Release, --no-restore)
Warnings: 0
Errors: 0
Engine tests passed: 198/198
Engine tests failed: 0
WPF: complete default runner exit 0, PASS WPF visual rules
Version: unchanged 4.1.0; built FileVersion 4.1.0.0
Production / tests / solution / NuGet / MCP config diff: none
External local files: 17/17 retained and backup hash verified
External git index files: 0
```

README 的 source-build 路徑与標準指令實際驗證，無版本升級、物理／runtime／Schema 8／模擬結果／UI 修改。LICENSE 為未改條款的 MIT；GitHub 自動識別待推送後檢查。QA／TODO／CHANGELOG 歷史原生未完成項目不因本輪自動化而改列 PASS。

上述 build／runner 為 OSS 整理時的執行結果；後續抽象規劃澄清只修改 Markdown，未重跑建置或測試，也未更動 sample JSON／builder。

README 本機連結 27 個全部存在、0 斷鏈；Git diff／cached diff 格式檢查通過。恢复的 MODEL_SPEC 正文（去除新增歷史說明並正規化換行）與原 Git 內容一致。最後 HEAD 仍為 `217c803`；新增 LICENSE／MODEL_SPEC／本報告與其他文件修改留在 working tree，參考目錄解除追蹤為 staged removal，沒有本輪 commit。

證據位於 ignored `output/v4.1.0-unified`：

| Log | SHA256 |
|---|---|
| oss-restore.log | `AF0CC24A784BA55A851DEE5EF2F2C08BD7AD239A1937FDE6C05F9814AD57966B` |
| oss-build.log | `AE983D6B5A02DE96E473BD44709608374EE00FB7AAE3743A61D9BF63210B1A5F` |
| oss-engine.log | `37BE6520ACF123474F680981C61AE218C9264238E0083FAA6F639D5837825DFC` |
| oss-wpf.log | `F3FD5CB88A6C0709F4F100B70BD50AD547B6233E25B1CAF07496A7EE4F1146C5` |

## F. Codex for Open Source Readiness

**NOT READY（以目前 GitHub 公開狀態評估）**：本地整理已完成，但遠端尚無 MIT、仍包含 external references，待核准提交／推送與遠端核對。大型機場線已確認為抽象規劃，不再列為實際里程來源權限阻擋項。公開更新完成後，文件／建置／測試基本條件可重新評估為 READY AFTER MINOR MANUAL ACTIONS；仍需真實申請材料，不保證獲選。

OpenAI 官方方案鼓勵核心維護者／重要生態系專案申請；不是以 stars 或是否有 SECURITY.md 單一 gate 決定。申請審查會考量使用、價值、維護活動與維護者角色，也可要求控制權核實；提出申請不保證取得資源。[Codex for Open Source](https://developers.openai.com/community/codex-for-oss)、[Program Terms](https://learn.chatgpt.com/docs/codex-for-oss-terms)。本專案的潛在價值是可檢視的離線 topology-aware 鐵路營運概念模擬與可重現 regression；這是申請論述方向，不是已證明廣泛影響力。
