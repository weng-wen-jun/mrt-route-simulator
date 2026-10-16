# 遠端原始碼與本機資料界線

GitHub主分支呈現目前維護的來源、測試、文件與範例。範例搬移時一併更新索引、測試、產生器及文件連結；舊路徑不在main重複保留。歷史提交與其他工作樹不因本輪整理而改寫、刪除或重設。

| 資料 | 遠端main | 本機 |
|---|---|---|
| src、tests、scripts、版本與規格文件 | 追蹤 | 保留 |
| samples小型／中型／大型與handbook-capacity | 追蹤整理後路徑 | 保留 |
| .codex/config.example.toml | 追蹤通用範本 | 保留 |
| .codex/config.toml | 不再追蹤 | 原檔保留，仍可指向現用MCP |
| artifacts、output、release、tmp、bin／obj | 忽略；需要時另作Release附件 | 保留建置、驗證與歷史證據 |
| 外部檔案參考 | 忽略 | 保留原始參考資料，不重新授權或上傳 |

本輪「隱藏」是讓遠端目前主分支不再追蹤本機專用檔，以及同步已完成的目錄搬移；不是修改GitHub倉庫可見性，也不是清除歷史版本。已經進入歷史提交的舊資料仍可從歷史查閱。本輪不改寫Git歷史。

新環境使用MCP時，可將 .codex/config.example.toml 複製成 .codex/config.toml，再調整cwd並完成Release建置。範本使用一般相對輸出路徑，不含維護者的本機磁碟或舊版artifact路徑；本機設定被忽略，後續變更不會再推到GitHub。
