using UnityEngine;
using UsefulAttribute;

namespace UsefulTools.View.Runtime
{
    public class ModelTargetTracker : MethodExecutorBehaviour
    {
        [SerializeField] private Transform _target;

        [SerializeField] private Vector3 _offset;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
        }

        // Update is called once per frame
        void Update()
        {
            transform.position = _target.position - _offset;
        }

        [MethodExecutor(true)]
        public void SetOffset()
        {
            _offset = _target.position - transform.position;
        }
    }
}