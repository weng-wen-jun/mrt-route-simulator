# 路線圖下緣頂層水平滑桿（2026-10-06）

## 需求與範圍

使用者要求左右滑桿回到路線圖下緣，但維持頂層；只有路線圖完全離開視窗時才隱藏。工作樹為 `D:/AI/codex/mrt-v403-integration`、V4.0.3，未修改 Engine、Schema、sample 或模擬結果。

## 實作

- 主視窗使用 Grid 頂層 Canvas（ZIndex10）承載既有水平 proxy，仍同步 RouteScrollViewer.HorizontalOffset。
- RouteViewportHost 在路線圖下緣預留18DIP；浮層貼齊 host 下緣。若下緣超出外層實際 ScrollContentPresenter viewport，改貼可見交集底部。完全無交集才隱藏；切到其他頁／子頁、無資料或無水平溢位仍隱藏。
- 外層 ScrollChanged 及 LayoutUpdated 更新定位，僅在座標有變時改 layout 屬性；不在 UI 重算 Engine 資料。

## 驗證紀錄

- 初次 Release build exit0，0警告／錯誤。
- 既有 `--outer-shell-only` exit0，含800×520 outer-top/bottom、route-top/bottom、水平雙向同步及大視窗填滿。
- 新增完整離開 viewport、浮層位置／層級、字體縮放及切頁回歸由子代理實作中；完整 WPF／Engine 與新版原生視窗尚待，不列全部完成。
- Engine完整runner exit0：198/198通過、0失敗。新增WPF測試首次整合build因測試缺少IsVisualDescendant helper，6處CS0103而FAIL；App已編譯成功，但不將整體build列PASS。已交回補足測試helper後重跑。
- helper補齊後Release0警告／錯誤；新focused測試scale1與0.8通過，1.25 compact/top FAIL：路線僅剩8.8px可見時Scrollbar模板最小高度將小滑桿撐出viewport。修正保持18DIP可操作高度、在viewport底部向上浮出（而非縮成sliver）；僅整張圖无交集才隱藏，不降低測試位置斷言。
- 邊界修正後Release0警告／錯誤、focused三種App縮放1／0.8／1.25全部exit0；完整default WPF runner exit0（含所有sample、播放、結果與輸出）。主代理複核代理diff，追加offscreen host保持非零面積斷言，測試-only geometry再build及focused重跑；App源碼不變。新版原生尚待，App縮放測試不等於OS DPI全矩陣。
- 非零host強化後再次Release0警告／錯誤、focused三種App縮放全部exit0。正式exe僅單次啟動window2954360，未建立新播放量測，sample14正常讀取中；原生畫面／拖曳仍待，不提前列PASS。
- 新版原生window2954360正常載入sample14、clock0，滑桿可見於路線圖下緣、狀態列上方。原生拖曳thumb後可見站由O01側移至O08–O23側，重新觀察UIA Value=815.9394703656999、clock仍0。正常切速度子頁滑桿消失、回Route後恢復原位置／同offset。限定原生位置／拖曳／切頁子項PASS，視窗保持開啟Route頁、無播放。未另存截圖或重新完成OS100/125/150% resize矩陣；完全離開viewport／部分可見及App縮放邊界由上述WPF回歸驗證。

## 原生量測收尾（改版前）

- 959228f2 於724.6s正常停止collector、確認「量測已停止」，再正常Alt+F4關閉舊正式視窗。兩個短run均aborted，completedRuns0，不做throughput或五輪記憶體PASS。此session早於本滑桿修正。
- 原始檔非覆寫歸檔於 artifacts/native-20261006，來源／複本SHA256相同。metrics `1EE2762C96885B984BB372569850126DEC8F1154E517EA40354B4CCD97A01123`；events `7DEABB4C91A4386424473EE1210E2BDE0527ABE333CCFDF2545CACA38D600285`。
- session累計63筆>100ms dispatcher gap；子代理原始行號審核／主代理重算：run1新增12筆、最大2167.1288ms，run2新增51筆、最大3213.8117ms。先前run2的63是累計counter，不能當run2增量。這是滑桿修正前互動播放timer evidence，不歸因本次滑桿新碼、不推定GC或OS輸入延遲。
