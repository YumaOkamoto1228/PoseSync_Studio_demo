using System;
using System.IO;
using UnityEngine;

namespace PoseSync
{
    /// <summary>指の曲げ(Mecanim の Stretched 筋肉値, +1=伸ばす / -1=曲げる)と開き。</summary>
    [Serializable]
    public struct HandShape
    {
        public float thumb, index, middle, ring, little;
        public float spread;       // 人差し指と中指を開く量(ピースの V)
        public float thumbSpread;  // 親指を人差し指から離す量(ハートの下側を作る)

        public static HandShape Lerp(HandShape a, HandShape b, float t) => new HandShape
        {
            thumbSpread = Mathf.Lerp(a.thumbSpread, b.thumbSpread, t),
            thumb = Mathf.Lerp(a.thumb, b.thumb, t),
            index = Mathf.Lerp(a.index, b.index, t),
            middle = Mathf.Lerp(a.middle, b.middle, t),
            ring = Mathf.Lerp(a.ring, b.ring, t),
            little = Mathf.Lerp(a.little, b.little, t),
            spread = Mathf.Lerp(a.spread, b.spread, t),
        };

        public static HandShape FromName(string name) => name switch
        {
            "peace" => new HandShape { thumb = -0.6f, index = 1f, middle = 1f, ring = -1f, little = -1f, spread = 1f },
            // 指は揃えて丸め(ハートの上側の丸み)、親指は伸ばして大きく開く(ハートの下側)
            "heart" => new HandShape { thumb = 1f, index = -0.35f, middle = -0.35f, ring = -0.4f, little = -0.45f, spread = -0.4f, thumbSpread = -1f },
            "open" => new HandShape { thumb = 0.8f, index = 1f, middle = 1f, ring = 1f, little = 1f, spread = 0.2f },
            "fist" => new HandShape { thumb = -0.8f, index = -1f, middle = -1f, ring = -1f, little = -1f, spread = 0f },
            _ => new HandShape { thumb = 0.3f, index = 0.3f, middle = 0.25f, ring = 0.2f, little = 0.15f, spread = 0f }, // relaxed
        };
    }

    /// <summary>片腕の姿勢。方向ベクトルはすべて画面座標系(x=画面右, y=上, z=カメラ側)。</summary>
    [Serializable]
    public struct ArmPose
    {
        public Vector3 upper, lower, fingers, palm;
        public HandShape shape;

        public static ArmPose Lerp(ArmPose a, ArmPose b, float t) => new ArmPose
        {
            upper = Vector3.Slerp(a.upper, b.upper, t),
            lower = Vector3.Slerp(a.lower, b.lower, t),
            fingers = Vector3.Slerp(a.fingers, b.fingers, t),
            palm = Vector3.Slerp(a.palm, b.palm, t),
            shape = HandShape.Lerp(a.shape, b.shape, t),
        };
    }

    /// <summary>タレントの全身姿勢(腕・手・背骨)。</summary>
    [Serializable]
    public struct TalentPose
    {
        public ArmPose left, right;   // 画面上の左右
        public float spineRoll;       // 度, +で頭が画面右へ
        public float spinePitch;      // 度, +でカメラ側へ前傾
        public float headRoll;        // 度, +で頭が画面右へ(首をかしげる)
        public Expression expression;

        public static TalentPose Lerp(TalentPose a, TalentPose b, float t) => new TalentPose
        {
            left = ArmPose.Lerp(a.left, b.left, t),
            right = ArmPose.Lerp(a.right, b.right, t),
            spineRoll = Mathf.Lerp(a.spineRoll, b.spineRoll, t),
            spinePitch = Mathf.Lerp(a.spinePitch, b.spinePitch, t),
            headRoll = Mathf.Lerp(a.headRoll, b.headRoll, t),
            expression = Expression.Lerp(a.expression, b.expression, t),
        };
    }

    /// <summary>表情の重み(0..1)。VRM の BlendShape プリセットに対応(TalentExpression が反映)。</summary>
    [Serializable]
    public struct Expression
    {
        public float blink_screen_left;   // 画面左側の目を閉じる(ウインク)
        public float blink_screen_right;  // 画面右側の目を閉じる
        public float joy;

        public static Expression Lerp(Expression a, Expression b, float t) => new Expression
        {
            blink_screen_left = Mathf.Lerp(a.blink_screen_left, b.blink_screen_left, t),
            blink_screen_right = Mathf.Lerp(a.blink_screen_right, b.blink_screen_right, t),
            joy = Mathf.Lerp(a.joy, b.joy, t),
        };
    }

    // ---- JSON ---------------------------------------------------------------

    [Serializable]
    public class ArmData
    {
        public float[] upper;
        public float[] lower;
        public string shape;
        public float[] fingers;
        public float[] palm;

        public ArmPose ToArmPose(float side)
        {
            var lowerDir = ToVector(lower, Vector3.down);
            return new ArmPose
            {
                upper = ToVector(upper, Vector3.down),
                lower = lowerDir,
                fingers = ToVector(fingers, lowerDir),
                palm = ToVector(palm, new Vector3(side, 0f, 0f)), // 指定なしは体の内側向き
                shape = HandShape.FromName(shape),
            };
        }

        static Vector3 ToVector(float[] v, Vector3 fallback) =>
            v != null && v.Length == 3 ? new Vector3(v[0], v[1], v[2]).normalized : fallback;
    }

    [Serializable]
    public class TalentPoseData
    {
        public ArmData screen_left;
        public ArmData screen_right;
        public float spine_roll;
        public float spine_pitch;
        public float head_roll;
        public Expression expression; // 省略時はすべて 0(通常の表情)
    }

    /// <summary>撮影者側の判定テンプレート。Unity では使わず、Python が読む。</summary>
    [Serializable]
    public class UserTemplateData
    {
        public ArmData screen_left;
        public ArmData screen_right;
        public float shoulder_roll;
        public bool mirror;
    }

    [Serializable]
    public class PoseDefinition
    {
        public int id;
        public string key;
        public string label;
        public TalentPoseData talent;
        public UserTemplateData[] user_templates;

        public TalentPose ToTalentPose() => new TalentPose
        {
            left = talent.screen_left.ToArmPose(+1f),
            right = talent.screen_right.ToArmPose(-1f),
            spineRoll = talent.spine_roll,
            spinePitch = talent.spine_pitch,
            headRoll = talent.head_roll,
            expression = talent.expression,
        };
    }

    [Serializable]
    public class PoseLibraryData
    {
        public int version;
        public PoseDefinition rest_pose;     // 待機中の立ち姿(判定結果ではない)
        public PoseDefinition fallback_pose; // 判定結果「おまかせ」
        public PoseDefinition[] poses;
    }

    /// <summary>StreamingAssets/poses.json を読む。Python 側も同じファイルからテンプレートを作る。</summary>
    public static class PoseLibrary
    {
        public const string FileName = "poses.json";

        static PoseLibraryData data;

        public static PoseLibraryData Data => data ??= Load();
        public static PoseDefinition Fallback => Data.fallback_pose;
        public static PoseDefinition Rest => Data.rest_pose != null && Data.rest_pose.talent != null && Data.rest_pose.talent.screen_left != null
            ? Data.rest_pose : Data.fallback_pose;
        public static int Count => Data.poses.Length;

        public static PoseDefinition Get(int id)
        {
            foreach (var p in Data.poses)
                if (p.id == id) return p;
            return Fallback;
        }

        public static void Reload() => data = Load();

        static PoseLibraryData Load()
        {
            var path = Path.Combine(Application.streamingAssetsPath, FileName);
            return JsonUtility.FromJson<PoseLibraryData>(File.ReadAllText(path));
        }
    }
}
