using System;
using Cysharp.Threading.Tasks;
using Kizami.Presentation.Runtime.MeshCut;
using UnityEngine;

namespace Kizami.View.Runtime.MeshCut
{
    public class Blade : MonoBehaviour, IBlade
    {
        [SerializeField] private MultiCutBlade _multiCutBlade;

        private bool _cutting = false;
        private UniTask _cutTask;
#if UNITY_EDITOR
        private IDisposable _obs;
#endif
        private void Start()
        {
#if UNITY_EDITOR
            _obs = DebugGUI.ObserveVariable("Cutting", () => _cutting.ToString());
#endif
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            _obs.Dispose();
#endif
        }

        public void SetRotation(Quaternion rotation)
        {
            transform.localRotation = rotation;
        }

        public void Cut(Action onComplete)
        {
            if (_cutting) return;

            _cutting = true;
            CutInternal(onComplete).Forget();
        }

        private async UniTaskVoid CutInternal(Action onComplete)
        {
            _cutting = true;

            try
            {
                await _multiCutBlade.CutAsync();

                onComplete?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                _cutting = false;
            }
        }
    }
}