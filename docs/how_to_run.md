# 動かし方

## 1. 判定サーバー(Python)を起動
PowerShell で:

```
cd C:\PoseSync_Studio\server
.venv\Scripts\python app.py
```

- `http://127.0.0.1:8765` で待ち受ける。起動に数秒かかる(モデルの読み込み)。
- 判定のたびにターミナルへ `[judge] heart_half d= 23.1 votes=[...] 340ms` のように出る。
- 判定に使ったフレームを残したい時は `--save-frames`(`data/judge_log/` に保存)。

## 2. Unity で Play
1. `Assets/PoseSync/Scenes/Booth.unity` を開く。
2. Game ビューの比率は何でもよい(中央に 1:1 で表示される)。1080×1080 にすると録画しやすい。
3. Play → スタート → 枠の中に立つ(両肩・両肘が枠に入ると自動で次へ)→ 見本ポーズ(Space で撮影へ)→ カウントダウン → 判定 → 結果。
   - Space = ボタンと同じ、Esc = 最初に戻る。
4. 画面左下のステータスに、接続状態と直近の判定(`votes` = 各フレームの判定、`server` / `total` = 遅延)が出る。

サーバーが起動していない時は自動でデバッグ判定に切り替わる(カウントダウン中に数字キー 1〜4 でポーズ指定、0 でおまかせ、押さなければランダム)。後からサーバーを起動しても、次の撮影から自動で使われる。

## 3. 単体の確認ツール(Unity を閉じた状態で)
| コマンド(`server` で実行) | 内容 |
|---|---|
| `.venv\Scripts\python tools\live_judge.py --note 名前` | リアルタイム判定表示 + 正解ラベル付きサンプル記録 |
| `.venv\Scripts\python tools\analyze_samples.py` | サンプルの集計 → `docs/validation.md` |
| `.venv\Scripts\python tools\distance_table.py` | テンプレート間距離表 → `docs/distance_table.md` |
| `.venv\Scripts\python tools\bench_judge.py` | 起動中サーバーへの判定遅延の計測 |
| `.venv\Scripts\python -m pytest tests -q` | テスト |

## 4. Unity をコマンドで再生成(エディタを閉じた状態で)
```
"C:\Program Files\Unity\Hub\Editor\6000.3.7f1\Editor\Unity.exe" -batchmode -quit -projectPath C:\PoseSync_Studio\PoseSync -executeMethod PoseSync.EditorTools.BoothSceneBuilder.BuildFromCommandLine -talent ayame
"C:\Program Files\Unity\Hub\Editor\6000.3.7f1\Editor\Unity.exe" -batchmode -quit -projectPath C:\PoseSync_Studio\PoseSync -executeMethod PoseSync.EditorTools.PosePreviewRenderer.Render
```
エディタからはメニュー PoseSync > Build Booth Scene / Render Pose Previews でも同じことができる。

## 5. 見た目の調整(BoothLook)
シーンの `Booth` オブジェクトにある **BoothLook** で、次の値を Play 中に動かして確認できる(毎フレーム反映)。
- 明るさ:`cameraGain`(カメラ映像・撮影写真の倍率、既定 1.2)、`talentLightIntensity`(既定 1.08)、`talentAmbient`
- タレント:`talentViewSize`(大きさ、既定 1273)、`talentCenterX`(体の中心の横位置、既定 0.74)、`talentOffsetY`(縦位置、既定 -400)。大きさを変える時は頭の位置を保つよう `offsetY ≒ 701 - 0.865 × talentViewSize` で合わせる
- 立ち位置の枠:`guideFrameX`(左右、既定 0.05〜0.55)、`guideFrameTop`、`guideColor`(既定 淡いピンク)

Play 中の変更は停止すると元に戻る。決まった値は控えておき、停止後に入れ直すか BoothSceneBuilder の初期値に反映する。
見た目は `PoseSync > Render Layout Preview`(→ `docs/layout_preview.png`)でも Play せずに確認できる。

## 6. 切り抜き・合成
- 判定サーバー(`app.py`)が起動していれば、撮影時に自動で切り抜いて背景と合成し、`data/photos/` に保存する。
- 背景を変えたい時は `PoseSync/Assets/StreamingAssets/backgrounds/background.png`(正方形推奨)を置く。
- 影の濃さ・ずらし量は `Booth` の **PhotoCompositor**(shadowOpacity / shadowOffset)。
- 切り抜きの品質だけを見る:Unity を閉じてカメラの前に立ち、`server` で `.venv\Scripts\python tools\cutout_check.py`(→ `docs/cutout_check.png`)。

## 7. 簡易ユーザーテスト
手順は [user_test_plan.md](user_test_plan.md)。要点:
- 判定サーバーは判定結果と骨格を `data/judge_log/` に自動で記録する(画像は `--save-frames` の時だけ)。
- Unity の **SessionLogger**(`Booth`)が体験ごとの記録を `data/sessions/日付.jsonl` に追記する。
  - 結果画面で記録係が `1`〜`4` / `0` を押すと「参加者がとったつもりのポーズ」を記録。
  - 待機画面で `N` を押すと次の参加者 ID へ。左下のステータスに記録状況が出る。
- 集計:`server` で `.venv\Scripts\python tools\analyze_sessions.py` → `docs/user_test.md`。

## 8. 切り抜き方式の比較
- 撮影するたびに元の写真が `data/photos/raw/` に保存される(PC 内のみ)。
- `server` で `.venv\Scripts\python tools\cutout_compare.py` → `docs/cutout_compare.png`(新しい6枚 × 4方式)。
- 良かった方式で起動する:例 `.venv\Scripts\python app.py --extra-seg deeplab`

## 9. シャッター演出
- 素材(再配布禁止)はリポジトリに含まれない。手元の動画から連番 PNG を作る:
  `server` で `.venv\Scripts\python tools\convert_shutter.py ..\shutter01\closecam.mov ..\shutter01\opencam.mov`
  → `PoseSync/Assets/ThirdParty/Shutter/Close|Open/`。その後 `PoseSync > Build Booth Scene`。
- 素材が無い時は白フラッシュになる。効果音を付ける時は `Booth` の **ShutterPlayer** の closeSound / openSound に設定する。
