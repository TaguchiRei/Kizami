using UnityEngine;

namespace UsefulTools.Infrastructure.Runtime
{
    public sealed class TouchArea : MonoBehaviour
    {
        [SerializeField] private string _groupName;

        public string GroupName => _groupName;
    }
}
