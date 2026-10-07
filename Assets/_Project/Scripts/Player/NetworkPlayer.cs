using Unity.Netcode;
using UnityEngine;

namespace CoreRush.Player
{
    public class NetworkPlayer : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Debug.Log("[NetworkPlayer] Player spawned for client " + OwnerClientId);
            }
        }
    }
}
