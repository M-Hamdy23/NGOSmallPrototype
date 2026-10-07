using _Project.Scripts.Game;
using _Project.Scripts.Objective;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace _Project.Scripts.Player
{
    public class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private float nearestAvailableCoreMaxDistance = 4f;
        public NetworkVariable<Team> playerTeam = new NetworkVariable<Team>(Team.None);
        public NetworkVariable<PlayerState> state = new NetworkVariable<PlayerState>(PlayerState.Alive);
        public NetworkVariable<ulong> carriedCoreId = new NetworkVariable<ulong>(0);

        public override void OnNetworkSpawn()
        {
            name = "Player_" + OwnerClientId;
            if (IsServer)
            {
                GameManager.Instance.ServerRegisterPlayer(OwnerClientId, this);
                Debug.Log($"[NetworkPlayer] Player spawned: client={OwnerClientId} team={playerTeam.Value} state={state.Value}");
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && GameManager.Instance != null)
            {
                GameManager.Instance.ServerUnregisterPlayer(OwnerClientId);
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.eKey.wasPressedThisFrame)
            {
                ServerRequestPickupOrInteract();
            }
        }

        private void ServerRequestPickupOrInteract()
        {
            if (carriedCoreId.Value != 0)
            {
                ObjectiveCore carried = ObjectiveCore.FindCoreById(carriedCoreId.Value);
                if (carried != null)
                {
                    carried.InteractionRequestServerRpc();
                    return;
                }
            }

            ObjectiveCore nearest = FindNearestAvailableCore(nearestAvailableCoreMaxDistance);
            if (nearest != null)
            {
                nearest.PickupRequestServerRpc();
            }
        }

        private ObjectiveCore FindNearestAvailableCore(float maxDistance)
        {
            ObjectiveCore best = null;
            float bestDist = maxDistance;
            foreach (ObjectiveCore core in ObjectiveCore.All)
            {
                if (core.state.Value != CoreState.Available) continue;
                float dist = Vector3.Distance(transform.position, core.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = core;
                }
            }

            return best;
        }
    }
}