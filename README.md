# PoseSync Studio(デモ)

ポーズ選択型 AI フォトブースの個人制作デモです。
撮影者がとったポーズを姿勢推定で判定し、タレントがそれに**対になるポーズで応えた**写真を撮れます(例: 撮影者がハートの半分を作ると、タレントが残りの半分を作る)。

- タレントは事前に定義した固定ポーズだけを取り、撮影者の動きはコピーしません(ポーズ破綻・不適切な動きが原理的に起きない)。
- 判定は上半身(肩・肘・手首)の向きをテンプレートと比べ、どれにも似ていなければ「おまかせポーズ」に切り替えます。
- 撮影した1枚から撮影者を切り抜き、背景を差し替えてタレントと合成した 1:1 の写真を作ります。

## 構成
| ディレクトリ | 内容 |
|---|---|
| `PoseSync/` | Unity 6 (6000.3, URP) プロジェクト。UI・体験フロー・タレント制御・合成 |
| `server/` | Python のローカルサーバー(FastAPI + MediaPipe)。姿勢推定・ポーズ判定・切り抜き |
| `docs/` | 設計の変更点・動かし方・検証結果 |

ポーズの定義(タレントの応答ポーズと判定テンプレート)は `PoseSync/Assets/StreamingAssets/poses.json` の1ファイルで、Unity と Python の両方が読みます。

## 動かし方(Windows)
### Python サーバー
```
cd server
py -3.10 -m venv .venv
.venv\Scripts\pip install -r requirements.txt
mkdir models
curl -L -o models\pose_landmarker_heavy.task https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_heavy/float16/latest/pose_landmarker_heavy.task
.venv\Scripts\python app.py
```

切り抜き方式の比較(`tools/cutout_compare.py`)や `app.py --extra-seg deeplab|multiclass` を使う時は、追加のモデルも置く:
```
curl -L -o models\deeplab_v3.tflite https://storage.googleapis.com/mediapipe-models/image_segmenter/deeplab_v3/float32/latest/deeplab_v3.tflite
curl -L -o models\selfie_multiclass_256x256.tflite https://storage.googleapis.com/mediapipe-models/image_segmenter/selfie_multiclass_256x256/float32/latest/selfie_multiclass_256x256.tflite
```

### Unity
1. Unity 6000.3 で `PoseSync/` を開く(UniVRM は OpenUPM から自動で入る)。
2. **VRM モデルは同梱していません。** 利用条件を守れる VRM(VRM0)を `PoseSync/Assets/ThirdParty/VRM/` に置く。
3. メニュー `PoseSync > Build Booth Scene` でシーンを生成し、`Assets/PoseSync/Scenes/Booth.unity` を Play。

詳しくは [docs/how_to_run.md](docs/how_to_run.md)、設計の経緯は [docs/design_changes.md](docs/design_changes.md)。

## 公開していないもの
- VRM モデル(配布条件により再配布禁止)と、そのレンダー画像
- 撮影写真・検証用サンプル(個人データ)
- MediaPipe のモデルファイル(上の手順でダウンロード)
