using Kizami.Application.Runtime.Player;
using UnityEngine;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;
using System;

namespace Kizami.Presentation.Runtime
{
    public class PlayerInfra : InitializableMonoBehaviour, IPlayerDataGateway, IPlayerInfra
    {
        public Vector3 Position => transform.position;

        public event Action UpdateEvent;
        
        public void Update()
        {
            UpdateEvent?.Invoke();
        }
    }
}