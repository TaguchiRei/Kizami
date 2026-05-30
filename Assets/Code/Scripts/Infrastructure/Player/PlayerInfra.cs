using Kizami.Application.Runtime.Player;
using UnityEngine;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace Kizami.Presentation.Runtime
{
    public class PlayerInfra : InitializableMonoBehaviour, IPlayerDataGateway
    {
        public Vector3 Position => transform.position;
        
        public void Update()
        {
            
        }
    }
}