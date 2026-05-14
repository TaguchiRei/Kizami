using System;

/// <summary>
/// 継承したクラスがルールを規定するクラスであることを保証するインターフェース
/// </summary>
public interface IRule
{
    /// <summary>
    /// このルールが規定するリザルトやエンディングに影響を与える状態
    /// 
    /// </summary>
    RuleState State { get; }
    event Action<RuleState> OnGameEndAction;

    public void StartGame();

    /// <summary> ゲームをポーズするときに使用 </summary>
    public void Pause();

    /// <summary> ポーズ解除に使用 </summary>
    public void Resume();

    /// <summary> このルールを停止するときに使用 </summary>
    public void Stop();
}

/// <summary>
/// ゲームルールにおける状態を規定する。
/// 書き換えてフラグ的に利用してもよし
/// </summary>
public enum RuleState
{
    Playing,
    GameOver,
    GameClear
}