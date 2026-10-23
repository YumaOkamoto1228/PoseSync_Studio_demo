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

## 使用素材(クレジット)
使用モデル:「雪花ラミィ」「百鬼あやめ」
風籟 ~Furai~ 様 (https://3d.nicovideo.jp/users/53694677)

ボイス:VOICEVOX 様「四国めたん」 (https://voicevox.hiroshiba.jp/)

カメラシャッター素材:みりんの動画素材 様 (https://miirriin.com/)

カメラ効果音:OtoLogic 様 (https://otologic.jp)

使用BGM:「Sunny Time」modus 様 (https://opentracks.com/bgm/detail/23907)

タレント切り替えアイコン:かくぬる素材工房 様 (https://knsoza1.com/2492.html)

**素材ファイルはすべて、このリポジトリでは配布していません。** 動かす時は各配布元から入手し、次の場所に置いてください。

| 素材 | 置き場所 |
|---|---|
| VRM モデル | `PoseSync/Assets/ThirdParty/VRM/` |
| カメラシャッター素材 | 動画から `server/tools/convert_shutter.py` で連番 PNG を作り `PoseSync/Assets/ThirdParty/Shutter/` へ |
| ボイス・カメラ効果音 | `PoseSync/Assets/ThirdParty/Voice/`(ファイル名は `docs/how_to_run.md` の「10. ガイド音声・効果音」) |
| BGM | `PoseSync/Assets/ThirdParty/Bgm/bgm_sunny_time.mp3` |
| タレント切り替えアイコン | `PoseSync/Assets/ThirdParty/Ui/talent_icon.png` |

## 公開していないもの
- 上記の素材ファイル一式と、タレントモデルのレンダー画像
- 撮影写真・検証用サンプル(個人データ)
- MediaPipe のモデルファイル(上の手順でダウンロード)
