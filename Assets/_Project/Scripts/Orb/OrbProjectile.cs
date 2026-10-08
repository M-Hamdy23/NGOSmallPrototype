using _Project.Scripts.Game;
using _Project.Scripts.Player;
using Unity.Netcode;
using UnityEngine;

namespace _Project.Scripts.Orb
{
    // Server physics only: the client never decides the hit result.
    // This component resolves collisions while the orb is in the Thrown state
    // and asks the server for elimination.
    public class OrbProjectile : MonoBehaviour
    {
        private SharedOrb _orb;

        private void Awake()
        {
            _orb = GetComponent<SharedOrb>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            if (_orb == null || _orb.state.Value != OrbState.Thrown) return;

            NetworkObject targetNetworkObject = collision.collider.GetComponentInParent<NetworkObject>();
            if (targetNetworkObject == null)
            {
                // Wall/floor/obstacle: flight ends, orb recovers to spawn.
                _orb.ServerReturnToSpawn();
                return;
            }

            NetworkPlayer target = targetNetworkObject.GetComponent<NetworkPlayer>();
            if (target == null)
            {
                _orb.ServerReturnToSpawn();
                return;
            }

            // Never hit the thrower (also covers the spawn-overlap frame).
            if (target.OwnerClientId == _orb.ServerThrowerClientId)
            {
                return;
            }

            if (target.state.Value != PlayerState.Alive)
            {
                _orb.ServerReturnToSpawn();
                return;
            }

            if (target.playerTeam.Value == Team.None || target.playerTeam.Value == _orb.ServerThrowerTeam())
            {
                // Teammate: no elimination, flight ends.
                _orb.ServerReturnToSpawn();
                return;
            }

            // Valid enemy hit -> server-authoritative elimination.
            if (GameManager.Instance != null)
            {
                GameManager.Instance.ServerEliminatePlayer(target);
            }
            _orb.ServerReturnToSpawn();
        }
    }
}
