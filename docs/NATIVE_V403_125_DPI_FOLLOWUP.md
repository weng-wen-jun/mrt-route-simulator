# 125% 畫面續驗（2026-10-03）

## 範圍與環境

- 使用者已將 App 所在螢幕切到125%，要求繼續指定比例畫面驗收。
- 透過 computer-use 的正常視窗／滑鼠／系統大小命令操作整合版 V4.0.3、sample14；未修改程式、Engine、Schema、版本或使用者存檔。
- 三個 session 均實測120 DPI／DpiScale1.25／interfaceScale1。全程維持暫停1171.2s；未Play／Reset，不冒充新完整播放輪次。
- 最大化及還原穩定畫面內容填滿，沒有重現上下居中白邊。DPI切換後首個操作的 refresh 曾無 accessibility，重新觀察後恢復；未沿用失效索引或把工具瞬態當App缺陷。

## 本輪通過子項

| 畫面 | 操作與結果 |
|---|---|
| 最大化／還原 | 運行圖可捲到O01與底部時間軸，內容不留先前上下白邊 |
| 精確800×520 DIP | 用正常系統大小模式縮到minimum，第二個session精確確認尺寸；控制列／分頁可換行，外層捲動可隱藏標題並到事件表／狀態區 |
| 小窗運行圖 | 上／中／下段掃描O26至O01，O08a/O15a及鄰站分離可達；時間軸可見，圖內水平滑桿可拖動 |
| 小窗Route | 固定水平滑桿在外層頂端與底部均存在；頁首直接拖動offset0→1870.986695，可見路線區段改變，不必先捲到底 |
| 小窗暫停列車 | 內層捲動至可見軌道後，FULL-O04右鍵命中，啟用同車跟隨；左鍵切入FULL-O04實際速度曲線，底部時間軸／站名可達，clock不變 |
| 窄高800×863.2 DIP | 第三個session確認尺寸；摘要展開後卡片文字換行且有內層捲軸，速度圖底部仍可達；運行圖O26／O01兩端、O15a/O08a及時間軸可捲到，水平滑桿可回到站名側 |

- 小窗前半段操作記錄於第一個session；第二個session用來核對同一尺寸800×520，不把第一個session起始1534.4×910.4當作全部操作尺寸。
- 本輪窄高未再重複Route列車左右鍵；該尺寸舊版布局續驗在`NATIVE_V403_125_DPI_PROGRESS.md`已有局部結果，本輪不冒充所有尺寸／所有子項全重驗。

## 證據歸檔

所有session均正常manual-stop／flush，completedRuns0。原檔留在exe的NativeAcceptanceLogs，以下副本在`docs/native-acceptance-raw/`，SHA256與來源逐一相符：

| stem（均為native-acceptance-前綴） | JSONL SHA256 |
|---|---|
| 20261003-064133463-0f3730c0b0e94c4f8129bc0dbbc2eaf8 | 22186E688FE3E83907F637ED032ECECC06CA343CFED6C0A8ADAB3F5E470B92CE |
| 20261003-064640981-9ca6e297e4a048eba8c66e115edb3171 | F4726B9472CD15328A6BFD22BEC235DA9F17936D4748A4F74BBEECC6C08BE46C |
| 20261003-064812834-71ac9849128543cf95b5baffc461eaf1 | DAC7DCA0F8DCDB88CCF95C92304BD21FF6D96544712961B6DB781F1620F845B9 |

三份`.events.jsonl`均空（沒有新Play），SHA256均E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855；不拿空事件檔證明物理事件repeat。

## 判定與後續

- 上表本輪畫面子項PASS，無新native視覺FAIL。App維持125%、800×863.2 DIP、摘要展開、運行圖下端、1171.2s暫停且量測停止。
- 不是完整125%矩陣結案：新版寬矮／其他指定尺寸與該尺寸列車hit-test交叉組合仍可補；100%／150%剩餘畫面組合依既有紀錄續驗。
- 本輪末嘗試正常Size模式＋固定Right／Up按鍵補寬矮；即時及重新擷取後仍維持原窄高画面，未取得尺寸變更成功證據，因此不列寬矮PASS，不把失效工具操作判為App resize FAIL。App仍維持上述窄高停留狀態。
- 完整input p50/p95/max、純resize處理延遲與多次cold launch尚待；本輪系統大小模式包含人工輸入／停留，不把整段時長當resize阻塞。
- 整體原生驗收仍NOT COMPLETED。未commit／push／tag／Release；純驗收未重跑已通過的完整build／Engine／WPF。
