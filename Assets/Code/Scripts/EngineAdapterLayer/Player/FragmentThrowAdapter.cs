using UnityEngine;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 視線の先の Brilliant Skill の言葉（BrilliantWord）をつかんでカメラの前に持ち、視線の先へ投げる Adapter。
    /// 言葉は全体を 1 つの的にして、丸ごとつかむ。重力を無視してまっすぐ、投擲の速さの _wordThrowSpeedScale 倍の速さで飛ぶ。
    /// </summary>
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    public sealed class FragmentThrowAdapter : InitializableMonoBehaviour
    {
        /// <summary> 実行順。CinemachineBrain（既定の0）より後 </summary>
        private const int EXECUTION_ORDER = 100;

        /// <summary> 言葉を狙う視線の円柱を、周りで調べるレイの数（中心のレイを除く） </summary>
        private const int AIM_RAY_COUNT = 8;

        [SerializeField]
        [Tooltip("飛ばす先を決めるレイが当たるレイヤー。プレイヤーのレイヤーは外す")]
        private LayerMask _aimLayers = ~0;

        [SerializeField, Min(0f)]
        [Tooltip("飛ばす先を決めるレイの長さ（m）。何にも当たらなければ、視線の先のこの距離の点へ飛ばす")]
        private float _aimDistance = 100f;

        [SerializeField, Min(0f)]
        [Tooltip("投げた言葉を消すまでの時間（秒）")]
        private float _flyingLifetime = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("言葉をつかめる距離（m）。言葉は遠くに大きく出る為、かけらより長い")]
        private float _wordGrabDistance = 80f;

        [SerializeField, Min(0f)]
        [Tooltip("言葉を狙う視線の太さ（半径, m）。視線を中心にこの半径の円柱が言葉の箱に当たればつかめる")]
        private float _wordGrabAimRadius = 1f;

        [SerializeField]
        [Tooltip("持っている言葉の中心を置く位置（カメラのローカル座標、m）")]
        private Vector3 _wordHoldOffset = new(0.5f, -0.3f, 1.6f);

        [SerializeField, Min(0.01f)]
        [Tooltip("持っている間の言葉の長さ（m）。箱の最も長い辺がこの長さになる大きさにする。投げた言葉は飛びながら出たときの大きさに戻る")]
        private float _wordHoldLength = 1.2f;

        [SerializeField, Min(0.01f)]
        [Tooltip("投げた言葉が出たときの大きさに戻るまでの時間（秒）。戻る間は当たり判定がない")]
        private float _wordGrowDuration = 0.35f;

        [SerializeField, Min(0f)]
        [Tooltip("投げた言葉の下端を、真下の面から空ける高さ（m）。地面に重ならないよう、戻る間に持ち上げる")]
        private float _wordGroundClearance = 0.5f;

        [SerializeField, Min(0.1f)]
        [Tooltip("言葉を投げる速さの、かけらの投擲の速さに対する倍率")]
        private float _wordThrowSpeedScale = 2f;

        private BrilliantSkillTextAdapter _wordSource;
        private BrilliantWord _heldWord;

        /// <summary> 言葉を持っているか </summary>
        public bool IsHolding => _heldWord != null;

        /// <summary>
        /// つかめる言葉の取得元を設定する。設定するまで、言葉はつかめない。
        /// </summary>
        /// <param name="wordSource">言葉を出している Adapter</param>
        public void SetWordSource(BrilliantSkillTextAdapter wordSource)
        {
            _wordSource = wordSource;
        }

        /// <summary>
        /// 視線の先にある言葉を、全体を 1 つの的として丸ごとつかむ。視線を中心にした太さ _wordGrabAimRadius の円柱を、
        /// カメラから _wordGrabDistance まで飛ばし、言葉の箱の当たり判定に最も近く当たった言葉をつかむ。投げられて飛んでいる言葉はつかめない。
        /// </summary>
        /// <returns>つかめたら true。既に持っているときと、つかめる言葉がないときは false</returns>
        public bool TryGrabWord()
        {
            if (!Initialized || _wordSource == null || IsHolding) return false;

            var cameraMain = Camera.main;
            if (cameraMain == null) return false;

            var cameraTransform = cameraMain.transform;
            BrilliantWord nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var word in _wordSource.Words)
            {
                if (word == null || word.IsThrown || word.IsCarried) continue;
                if (!TryHitAim(word.Box, cameraTransform, out var distance) || distance >= nearestDistance) continue;

                nearest = word;
                nearestDistance = distance;
            }

            if (nearest == null) return false;

            nearest.BeginCarry(_wordHoldLength);
            nearest.FollowCamera(cameraTransform, _wordHoldOffset);
            _heldWord = nearest;
            return true;
        }

        /// <summary>
        /// 持っている言葉を、視線の先へ投げる。言葉は重力なしで速く飛ぶ。
        /// </summary>
        /// <param name="speed">投擲の初速（m/s）。言葉は _wordThrowSpeedScale 倍で飛ぶ</param>
        /// <returns>投げたら true。持っていないときは false</returns>
        public bool TryThrow(float speed)
        {
            if (!Initialized || _heldWord == null) return false;

            ThrowWord(speed * _wordThrowSpeedScale);
            return true;
        }

        private void LateUpdate()
        {
            if (!Initialized || _heldWord == null) return;

            var cameraMain = Camera.main;
            if (cameraMain == null) return;

            _heldWord.FollowCamera(cameraMain.transform, _wordHoldOffset);
        }

        /// <summary>
        /// 持っている言葉を、中心から視線の先の狙った点へ向けて飛ばす。
        /// </summary>
        private void ThrowWord(float speed)
        {
            var word = _heldWord;
            _heldWord = null;

            var cameraMain = Camera.main;
            var direction = cameraMain != null
                ? (GetAimPoint(cameraMain.transform) - word.Center).normalized
                : word.transform.forward;
            word.Launch(direction, speed, _flyingLifetime, _wordGrowDuration, _wordGroundClearance);
        }

        /// <summary>
        /// カメラの視線を中心にした円柱（太さ _wordGrabAimRadius、長さ _wordGrabDistance）が、箱に当たるか。
        /// 視線と、視線に垂直な 8 方向へ半径だけずらした平行なレイの、計 9 本で調べ、最も近い当たりまでの距離を返す。
        /// </summary>
        private bool TryHitAim(BoxCollider box, Transform cameraTransform, out float distance)
        {
            distance = float.MaxValue;
            var forward = cameraTransform.forward;
            var found = false;

            for (var i = -1; i < AIM_RAY_COUNT; i++)
            {
                var offset = Vector3.zero;
                if (i >= 0)
                {
                    var angle = i * Mathf.PI * 2f / AIM_RAY_COUNT;
                    offset = (cameraTransform.right * Mathf.Cos(angle) + cameraTransform.up * Mathf.Sin(angle)) *
                             _wordGrabAimRadius;
                }

                var ray = new Ray(cameraTransform.position + offset, forward);
                if (!box.Raycast(ray, out var hit, _wordGrabDistance) || hit.distance >= distance) continue;

                distance = hit.distance;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// 視線の先でレイが当たった点を返す。何にも当たらなければ、視線の先の _aimDistance の点を返す。
        /// </summary>
        private Vector3 GetAimPoint(Transform cameraTransform)
        {
            var origin = cameraTransform.position;
            var forward = cameraTransform.forward;

            return Physics.Raycast(origin, forward, out var hit, _aimDistance, _aimLayers, QueryTriggerInteraction.Ignore)
                ? hit.point
                : origin + forward * _aimDistance;
        }
    }
}
