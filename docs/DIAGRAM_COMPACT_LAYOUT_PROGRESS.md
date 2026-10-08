# 運行圖最小圖高與操作區收折

## 需求與範圍（2026-10-06）

使用者在實測96DPI／800×520 DIP原生視窗指出：事件列表未收折時，運行圖的可視高度過小，連垂直滑桿都難操作。要求加大圖區最小尺寸，並將上方篩選、縮放、刻度與匯出控制改為可收折。

- 僅修改WPF Presentation；不改Engine、Schema、模擬時刻、軌跡、匯出資料或版本。
- 保留原控制項名稱、事件處理與設定；上方操作區預設展開，可手動收折。
- 小視窗需要的額外高度必須納入外層ShellScrollViewer可捲動範圍，不用最小圖高把事件表擠到無法到達的位置。
- 保留固定時間軸、兩軸独立縮放、事件列表獨立收折及Route頂層滑桿。其他分頁維持既有720 DIP compact內容政策。

## 修改前原生證據

獨立短session71a5f935，96DPI／800×520 DIP；暫停1134.2s，圖內vertical202.48／horizontal92、終點01:59:22.6可見。事件列表展開時圖區明顯不足；原始metrics/events已正常停止並SHA256一致非覆寫歸檔。詳見[NATIVE_V403_20261006_CONTINUOUS_ACCEPTANCE.md](NATIVE_V403_20261006_CONTINUOUS_ACCEPTANCE.md)。

## 實作

- `MainWindow.xaml`新增DiagramControlsExpander、DiagramWorkspaceGrid及DiagramViewportBorder；完整保留原控制設定／handlers。圖框MinHeight344、內層ScrollViewer MinHeight300 DIP，事件表仍可獨立收折。
- `MainWindow.ShellScroll.cs`僅在Diagram選中時按必要控制／最小圖框／事件表高度增加外層extent；初次measure、展開收折、寬窄換行及切頁同步。額外高度不採圖框stretch DesiredSize，避免把上一輪高度累積進下一次；reentrancy及.01DIP變更guard保留。

## 驗證

- 新增DiagramCompactLayoutTests：800×520、軟體scale1／0.8／1.25、控制／事件四種fold狀態；圖框最小高度、圖內viewport、外層可達底端、縮放設定保留、寬高窗填滿／復原，以及切回動畫。
- 首次Release build成功但新增tests有2個nullable警告；主代理修正explicit null guard後再build，0警告／0錯誤。
- WPF `--diagram-compact-only`三種軟體scale、四種fold與寬高／復原／切頁皆exit0。
- Engine完整runner198/198通過、0失敗；完整default WPF runner exit0，包含sticky axis、獨立zoom、Route overlay、專案讀取、匯出及新增compact項。
- 新binary原生800×520圖高／操作收折／滑桿待驗；軟體scale回歸不等於OS DPI矩陣。

目前狀態：實作、Release及完整回歸完成。2026-10-07使用者已手動縮窗，App量測確認96DPI／800×520 DIP；四種fold、兩滑桿、外層底端、固定時間軸與設定保留完成有限原生檢查，見[NATIVE_ACCEPTANCE_20261007_MCP.md](NATIVE_ACCEPTANCE_20261007_MCP.md)。未宣稱全DPI、正式互動效能或整體驗收完成。
