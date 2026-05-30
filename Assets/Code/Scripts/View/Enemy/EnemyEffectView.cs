using UnityEngine;
using UnityEngine.VFX;
using UsefulTools.Utility.Runtime.Utility;
using UsefulTools.UtilityUnity.Runtime.Pause;

namespace Kizami.View.Runtime
{
    public class EnemyEffectView : MonoBehaviour, IRecyclable, IPausable
    {
        public int RecycleId { get; set; }
        public bool IsPaused { get; private set; }

        [SerializeField] private VisualEffect _killVfx;

        private const string ENEMY_EFFECT_SPAWN_POINT = "SpawnPosition";
        private const string ENEMY_EFFECT_PLAY_KEY_WORD = "PlayEffect";

        public void OnRecycle()
        {
            _killVfx.Stop();
        }

        public void PlayEffect(Vector3 position)
        {
            _killVfx.SetVector3(ENEMY_EFFECT_SPAWN_POINT, position);
            _killVfx.Play();
        }


        public void Pause()
        {
            _killVfx.pause = true;
            IsPaused = true;
        }

        public void Resume()
        {
            _killVfx.pause = false;
            IsPaused = false;
        }
    }
}