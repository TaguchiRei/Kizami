namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の動き方。移動の Job が読み、フィニッシャーの攻撃（EnemyFinisherAttack）が切り替える。
    /// </summary>
    public enum EnemyMoveMode
    {
        /// <summary> 隊列か距離マップに沿って歩き、足場がなくなれば落ちる </summary>
        Walking,

        /// <summary> その場で止まる。フィニッシャーに吸収の対象として選ばれた敵 </summary>
        Held,

        /// <summary> 地面に立つ処理と重力を止め、EnemyAgent.FlyTarget へまっすぐ飛ぶ </summary>
        Flying
    }
}
