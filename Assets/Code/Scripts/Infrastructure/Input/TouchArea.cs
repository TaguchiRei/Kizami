using UnityEngine;

namespace UsefulTools.Infrastructure.Runtime
{
    public sealed class TouchArea : MonoBehaviour
    {
        [SerializeField] private string _groupId;

        public string GroupId => _groupId;
    }
}