using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// スキルと飛び移りの効果を、エンジン側で実行させる出来事を流すチャンネル。PlayerInitializer が1つ作り、PlayerEventBoard へは購読面（IPlayerSkillEffectEvents）で登録して、
    /// 発行側の Service には具象を渡す。案内のログに使う IPlayerActionEvents とは用途を分け、効果ごとの値を持つ。
    /// </summary>
    public sealed class PlayerSkillEffectEvents : IPlayerSkillEffectEvents
    {
        private readonly ActionChannel<BeamEffectNotice> _onBeam = new();
        private readonly ActionChannel<float> _onExplosion = new();
        private readonly ActionChannel<SphereCarveNotice> _onSphereCarve = new();
        private readonly ActionChannel<SweepEffectNotice> _onSweep = new();
        private readonly ActionChannel<CapsuleCarveNotice> _onCapsuleCarve = new();
        private readonly ActionChannel<BombEffectNotice> _onBomb = new();
        private readonly ActionChannel<string> _onArmAnimation = new();

        public IActionChannel<BeamEffectNotice> OnBeam => _onBeam;
        public IActionChannel<float> OnExplosion => _onExplosion;
        public IActionChannel<SphereCarveNotice> OnSphereCarve => _onSphereCarve;
        public IActionChannel<SweepEffectNotice> OnSweep => _onSweep;
        public IActionChannel<CapsuleCarveNotice> OnCapsuleCarve => _onCapsuleCarve;
        public IActionChannel<BombEffectNotice> OnBomb => _onBomb;
        public IActionChannel<string> OnArmAnimation => _onArmAnimation;

        /// <summary>
        /// 視線の向きへビームを出させる。
        /// </summary>
        public void RaiseBeam(in BeamEffectNotice notice)
        {
            _onBeam.Invoke(notice);
        }

        /// <summary>
        /// カメラの位置を中心に爆発を出させる。
        /// </summary>
        /// <param name="radius">爆発の半径（m）</param>
        public void RaiseExplosion(float radius)
        {
            _onExplosion.Invoke(radius);
        }

        /// <summary>
        /// 指定した点を中心に球で削らせる。
        /// </summary>
        public void RaiseSphereCarve(in SphereCarveNotice notice)
        {
            _onSphereCarve.Invoke(notice);
        }

        /// <summary>
        /// 正面を横長に削らせる。
        /// </summary>
        public void RaiseSweep(in SweepEffectNotice notice)
        {
            _onSweep.Invoke(notice);
        }

        /// <summary>
        /// 2点を結ぶカプセルで装甲ごと削らせる。
        /// </summary>
        public void RaiseCapsuleCarve(in CapsuleCarveNotice notice)
        {
            _onCapsuleCarve.Invoke(notice);
        }

        /// <summary>
        /// 爆弾を投げさせる。
        /// </summary>
        public void RaiseBomb(in BombEffectNotice notice)
        {
            _onBomb.Invoke(notice);
        }

        /// <summary>
        /// 腕のアニメーションを再生させる。
        /// </summary>
        /// <param name="trigger">Animator のトリガー名。空なら再生しない</param>
        public void RaiseArmAnimation(string trigger)
        {
            _onArmAnimation.Invoke(trigger);
        }
    }

    /// <summary>
    /// スキルと飛び移りの効果を実行させる出来事の購読面。
    /// </summary>
    public interface IPlayerSkillEffectEvents : IEvent
    {
        /// <summary> 視線の向きへビームを出すときに流れる </summary>
        IActionChannel<BeamEffectNotice> OnBeam { get; }

        /// <summary> カメラの位置を中心に爆発を出すときに流れる。値は半径（m） </summary>
        IActionChannel<float> OnExplosion { get; }

        /// <summary> 指定した点を中心に球で削るとき（飛び移りの経路）に流れる </summary>
        IActionChannel<SphereCarveNotice> OnSphereCarve { get; }

        /// <summary> 正面を横長に削るときに流れる </summary>
        IActionChannel<SweepEffectNotice> OnSweep { get; }

        /// <summary> 2点を結ぶカプセルで装甲ごと削るとき（攻走の経路）に流れる </summary>
        IActionChannel<CapsuleCarveNotice> OnCapsuleCarve { get; }

        /// <summary> 爆弾を投げるときに流れる </summary>
        IActionChannel<BombEffectNotice> OnBomb { get; }

        /// <summary> スキルを発動したときに流れる。値は腕の Animator のトリガー名 </summary>
        IActionChannel<string> OnArmAnimation { get; }
    }

    /// <summary>
    /// ビームの長さと半径。
    /// </summary>
    public readonly struct BeamEffectNotice
    {
        /// <summary> カプセルの長さ（m） </summary>
        public float Length { get; }

        /// <summary> カプセルの半径（m） </summary>
        public float Radius { get; }

        public BeamEffectNotice(float length, float radius)
        {
            Length = length;
            Radius = radius;
        }
    }

    /// <summary>
    /// 球で削る中心と半径。
    /// </summary>
    public readonly struct SphereCarveNotice
    {
        /// <summary> 球の中心（ワールド空間） </summary>
        public Vector3 Center { get; }

        /// <summary> 球の半径（m） </summary>
        public float Radius { get; }

        public SphereCarveNotice(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }
    }

    /// <summary>
    /// 正面を横長に削る範囲。
    /// </summary>
    public readonly struct SweepEffectNotice
    {
        /// <summary> 横幅（m） </summary>
        public float Width { get; }

        /// <summary> 奥行き（m） </summary>
        public float Depth { get; }

        /// <summary> 1m あたりに減る強さ </summary>
        public float Falloff { get; }

        /// <summary> 範囲の中心を正面へずらす距離（m） </summary>
        public float ForwardOffset { get; }

        public SweepEffectNotice(float width, float depth, float falloff, float forwardOffset)
        {
            Width = width;
            Depth = depth;
            Falloff = falloff;
            ForwardOffset = forwardOffset;
        }
    }

    /// <summary>
    /// 2点を結ぶカプセルで装甲ごと削る範囲。
    /// </summary>
    public readonly struct CapsuleCarveNotice
    {
        /// <summary> カプセルの一方の端（ワールド空間） </summary>
        public Vector3 Start { get; }

        /// <summary> カプセルのもう一方の端（ワールド空間） </summary>
        public Vector3 End { get; }

        /// <summary> カプセルの半径（m） </summary>
        public float Radius { get; }

        /// <summary> 装甲の遮断を判定するレイの始点 </summary>
        public Vector3 Origin { get; }

        /// <summary> 1m あたりに減る強さ </summary>
        public float Falloff { get; }

        public CapsuleCarveNotice(Vector3 start, Vector3 end, float radius, Vector3 origin, float falloff)
        {
            Start = start;
            End = end;
            Radius = radius;
            Origin = origin;
            Falloff = falloff;
        }
    }

    /// <summary>
    /// 爆弾の投げ方と爆発の大きさ。
    /// </summary>
    public readonly struct BombEffectNotice
    {
        /// <summary> 平らな地面に落ちる距離（m） </summary>
        public float Distance { get; }

        /// <summary> 爆発の半径（m） </summary>
        public float Radius { get; }

        /// <summary> 投げる起点を正面へずらす距離（m） </summary>
        public float ForwardOffset { get; }

        public BombEffectNotice(float distance, float radius, float forwardOffset)
        {
            Distance = distance;
            Radius = radius;
            ForwardOffset = forwardOffset;
        }
    }
}
