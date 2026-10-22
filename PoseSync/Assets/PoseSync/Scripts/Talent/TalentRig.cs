using System.Collections.Generic;
using UnityEngine;

namespace PoseSync
{
    /// <summary>
    /// タレントの姿勢を「画面座標系の方向ベクトル」で制御する(腕・手首・指・背骨)。
    /// 撮影者の動きは一切コピーせず、事前定義のポーズ間を補間するだけ(§9)。
    /// Humanoid の Animator があれば骨を自動取得する。無ければ Inspector で直接指定する(仮マネキン用、指は動かない)。
    /// </summary>
    [DefaultExecutionOrder(-500)] // VRM の SpringBone 等より先に骨を確定させる
    public class TalentRig : MonoBehaviour
    {
        [Tooltip("ポーズの「画面座標」を決めるカメラ")]
        public Camera viewCamera;
        public bool autoFromAnimator = true;

        [Header("Bones (画面上の左右)")]
        public Transform hips;
        public Transform spine;
        public Transform head;
        public Transform leftUpper, leftLower, leftHand;
        public Transform rightUpper, rightLower, rightHand;

        [Header("Body motion (待機モーション等が書き込む)")]
        public Vector3 hipsOffset;
        public float hipsRoll;

        public TalentPose Current { get; private set; }
        public bool IsTransitioning => transitionTime < transitionDuration;

        TalentPose transitionFrom, transitionTo;
        float transitionTime, transitionDuration;

        struct Rest
        {
            public Quaternion rotation; // root 基準
            public Vector3 direction;   // root 基準、骨→子
        }

        struct HandRest
        {
            public Quaternion rotation; // root 基準
            public Vector3 fingers;     // root 基準、手首→中指
            public Vector3 palm;        // root 基準、手のひらの法線
        }

        Rest restLU, restLL, restRU, restRL;
        HandRest restLH, restRH;
        Vector3 restHipsLocalPos;
        Quaternion restHipsLocalRot, restSpineLocalRot, restHeadLocalRot;
        bool ready;

        // 指(Humanoid の筋肉値)
        Animator animator;
        HumanPoseHandler poseHandler;
        HumanPose humanPose;
        bool screenLeftIsAnatomicalLeft;
        /// <summary>画面左に見える側が解剖学的な左か(表情のウインクの左右対応に使う)。</summary>
        public bool ScreenLeftIsAnatomicalLeft => screenLeftIsAnatomicalLeft;
        int[] leftFingerMuscles, rightFingerMuscles; // FingerSlot 順

        void Awake() => Initialize();

        /// <summary>基準姿勢を記録する。エディタからのプレビュー描画でも使う。</summary>
        public void Initialize()
        {
            if (ready) return;
            if (autoFromAnimator) ResolveFromAnimator();
            if (leftUpper == null || rightUpper == null)
            {
                Debug.LogError("[TalentRig] 腕の骨が見つかりません", this);
                return;
            }
            // 起動時の姿勢(VRM は T ポーズ、手のひらは下向き)を基準姿勢として記録する
            restLU = CaptureRest(leftUpper, leftLower);
            restLL = CaptureRest(leftLower, leftHand);
            restRU = CaptureRest(rightUpper, rightLower);
            restRL = CaptureRest(rightLower, rightHand);
            restLH = CaptureHandRest(leftHand, restLL.direction, screenLeftIsAnatomicalLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            restRH = CaptureHandRest(rightHand, restRL.direction, screenLeftIsAnatomicalLeft ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            if (hips != null)
            {
                restHipsLocalPos = hips.localPosition;
                restHipsLocalRot = hips.localRotation;
            }
            if (spine != null) restSpineLocalRot = spine.localRotation;
            if (head != null) restHeadLocalRot = head.localRotation;
            SetupFingers();

            Current = transitionTo = PoseLibrary.Rest.ToTalentPose();
            ready = true;
        }

        void ResolveFromAnimator()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;

            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform aU = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform aL = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform aH = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform bU = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform bL = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform bH = animator.GetBoneTransform(HumanBodyBones.RightHand);

            // 正面向きなら解剖学的な右腕が画面左に来るが、向きに依存しないよう実際の画面位置で決める
            screenLeftIsAnatomicalLeft = ScreenX(aU) < ScreenX(bU);
            (leftUpper, leftLower, leftHand) = screenLeftIsAnatomicalLeft ? (aU, aL, aH) : (bU, bL, bH);
            (rightUpper, rightLower, rightHand) = screenLeftIsAnatomicalLeft ? (bU, bL, bH) : (aU, aL, aH);
        }

        float ScreenX(Transform t) => viewCamera != null
            ? viewCamera.WorldToViewportPoint(t.position).x
            : Vector3.Dot(t.position, Vector3.right);

        Rest CaptureRest(Transform bone, Transform child)
        {
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            return new Rest
            {
                rotation = inv * bone.rotation,
                direction = inv * (child.position - bone.position).normalized,
            };
        }

        HandRest CaptureHandRest(Transform hand, Vector3 forearmDir, HumanBodyBones middleProximal)
        {
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            Vector3 fingers = forearmDir;
            var middle = animator != null && animator.isHuman ? animator.GetBoneTransform(middleProximal) : null;
            if (middle != null) fingers = inv * (middle.position - hand.position).normalized;
            return new HandRest
            {
                rotation = inv * hand.rotation,
                fingers = fingers,
                palm = Vector3.down, // Humanoid の T ポーズは手のひらが下
            };
        }

        // ---- ポーズ指定 ------------------------------------------------------

        /// <summary>現在の姿勢から duration 秒かけて pose へ移る。</summary>
        public void TransitionTo(TalentPose pose, float duration)
        {
            transitionFrom = Current;
            transitionTo = pose;
            transitionTime = 0f;
            transitionDuration = Mathf.Max(duration, 1e-4f);
        }

        /// <summary>補間なしで即座に適用する(待機モーションなど毎フレーム書き込む用途)。</summary>
        public void SetImmediate(TalentPose pose)
        {
            Current = transitionFrom = transitionTo = pose;
            transitionTime = transitionDuration = 0f;
        }

        void Update()
        {
            if (transitionTime < transitionDuration)
            {
                transitionTime += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(transitionTime / transitionDuration));
                Current = TalentPose.Lerp(transitionFrom, transitionTo, t);
            }
        }

        // ---- 骨への適用 ------------------------------------------------------

        void LateUpdate() => ApplyBones();

        public void ApplyBones()
        {
            if (!ready) return;

            if (hips != null)
            {
                hips.localPosition = restHipsLocalPos + hipsOffset;
                hips.localRotation = restHipsLocalRot * Quaternion.Euler(0f, 0f, hipsRoll);
            }
            if (spine != null) spine.localRotation = restSpineLocalRot;
            if (head != null) head.localRotation = restHeadLocalRot;

            ApplyFingers(Current.left.shape, Current.right.shape);

            if (spine != null)
            {
                Transform cam = CameraTransform;
                Vector3 forward = cam != null ? cam.forward : Vector3.back;
                Vector3 right = cam != null ? cam.right : Vector3.left;
                spine.rotation = Quaternion.AngleAxis(-Current.spineRoll, forward)
                               * Quaternion.AngleAxis(-Current.spinePitch, right)
                               * spine.rotation;
            }
            if (head != null)
            {
                Transform cam = CameraTransform;
                head.rotation = Quaternion.AngleAxis(-Current.headRoll, cam != null ? cam.forward : Vector3.back) * head.rotation;
            }

            // 親→子の順にワールド回転を決める
            ApplyBone(leftUpper, restLU, Current.left.upper);
            ApplyBone(leftLower, restLL, Current.left.lower);
            ApplyHand(leftHand, restLH, Current.left);
            ApplyBone(rightUpper, restRU, Current.right.upper);
            ApplyBone(rightLower, restRL, Current.right.lower);
            ApplyHand(rightHand, restRH, Current.right);
        }

        Transform CameraTransform => viewCamera != null ? viewCamera.transform : null;

        void ApplyBone(Transform bone, Rest rest, Vector3 screenDir)
        {
            Quaternion root = transform.rotation;
            Vector3 from = root * rest.direction;
            Vector3 to = ScreenToWorld(screenDir);
            bone.rotation = Quaternion.FromToRotation(from, to) * (root * rest.rotation);
        }

        void ApplyHand(Transform hand, HandRest rest, ArmPose arm)
        {
            Quaternion root = transform.rotation;
            Vector3 f = ScreenToWorld(arm.fingers);
            Vector3 p = Vector3.ProjectOnPlane(ScreenToWorld(arm.palm), f);
            if (p.sqrMagnitude < 1e-4f) return; // 指先と手のひらが平行 = 向きが決まらない
            Quaternion restFrame = Quaternion.LookRotation(root * rest.fingers, -(root * rest.palm));
            Quaternion targetFrame = Quaternion.LookRotation(f, -p.normalized);
            hand.rotation = targetFrame * Quaternion.Inverse(restFrame) * (root * rest.rotation);
        }

        Vector3 ScreenToWorld(Vector3 d)
        {
            if (viewCamera == null) return new Vector3(-d.x, d.y, d.z); // カメラが +Z 側から -Z を見ている前提
            Transform c = viewCamera.transform;
            return (c.right * d.x + c.up * d.y - c.forward * d.z).normalized;
        }

        // ---- 指 ------------------------------------------------------------

        enum FingerSlot { Thumb1, Thumb2, Thumb3, Index1, Index2, Index3, Middle1, Middle2, Middle3, Ring1, Ring2, Ring3, Little1, Little2, Little3, IndexSpread, MiddleSpread, ThumbSpread, Count }

        void SetupFingers()
        {
            if (animator == null || !animator.isHuman || animator.avatar == null) return;
            poseHandler = new HumanPoseHandler(animator.avatar, animator.transform);

            var names = new Dictionary<string, int>();
            for (int i = 0; i < HumanTrait.MuscleCount; i++) names[HumanTrait.MuscleName[i]] = i;

            int[] Build(string side)
            {
                var idx = new int[(int)FingerSlot.Count];
                string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
                for (int f = 0; f < 5; f++)
                    for (int j = 1; j <= 3; j++)
                        idx[f * 3 + j - 1] = names.TryGetValue($"{side} {fingers[f]} {j} Stretched", out int m) ? m : -1;
                idx[(int)FingerSlot.IndexSpread] = names.TryGetValue($"{side} Index Spread", out int s1) ? s1 : -1;
                idx[(int)FingerSlot.ThumbSpread] = names.TryGetValue($"{side} Thumb Spread", out int s3) ? s3 : -1;
                idx[(int)FingerSlot.MiddleSpread] = names.TryGetValue($"{side} Middle Spread", out int s2) ? s2 : -1;
                return idx;
            }

            var anatomicalLeft = Build("Left");
            var anatomicalRight = Build("Right");
            leftFingerMuscles = screenLeftIsAnatomicalLeft ? anatomicalLeft : anatomicalRight;
            rightFingerMuscles = screenLeftIsAnatomicalLeft ? anatomicalRight : anatomicalLeft;
        }

        void ApplyFingers(HandShape left, HandShape right)
        {
            if (poseHandler == null) return;
            poseHandler.GetHumanPose(ref humanPose);
            WriteHand(leftFingerMuscles, left);
            WriteHand(rightFingerMuscles, right);
            poseHandler.SetHumanPose(ref humanPose);
        }

        void WriteHand(int[] idx, HandShape s)
        {
            float[] curl = { s.thumb, s.index, s.middle, s.ring, s.little };
            for (int f = 0; f < 5; f++)
                for (int j = 0; j < 3; j++)
                    Set(idx[f * 3 + j], curl[f]);
            // ピースの V: 人差し指と中指を逆方向に開く
            Set(idx[(int)FingerSlot.IndexSpread], s.spread);
            Set(idx[(int)FingerSlot.ThumbSpread], s.thumbSpread);
            Set(idx[(int)FingerSlot.MiddleSpread], -s.spread);
        }

        void Set(int muscle, float value)
        {
            if (muscle >= 0) humanPose.muscles[muscle] = value;
        }

        void OnDestroy() => poseHandler?.Dispose();
    }
}
