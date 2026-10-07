using System.Collections.Generic;
using _Project.Scripts.Game;
using _Project.Scripts.Objective;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _Project.Scripts.Player
{
    public class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private float nearestAvailableCoreMaxDistance = 4f;

        public NetworkVariable<Team> playerTeam = new NetworkVariable<Team>(Team.None);
        public NetworkVariable<PlayerState> state = new NetworkVariable<PlayerState>(PlayerState.Alive);
        public NetworkVariable<ulong> carriedCoreId = new NetworkVariable<ulong>(0);
        public NetworkVariable<bool> hasOrb = new NetworkVariable<bool>(false);

        public static IReadOnlyList<NetworkPlayer> All => Instances;
        private static readonly List<NetworkPlayer> Instances = new List<NetworkPlayer>();

        private Renderer _renderer;
        private static readonly Color ColorRed = new Color(0.85f, 0.25f, 0.25f);
        private static readonly Color ColorBlue = new Color(0.25f, 0.4f, 0.9f);
        private static readonly Color ColorEliminated = new Color(0.35f, 0.35f, 0.35f);

        private void OnEnable()
        {
            Instances.Add(this);
        }

        private void OnDisable()
        {
            Instances.Remove(this);
        }

        public override void OnNetworkSpawn()
        {
            name = "Player_" + OwnerClientId;
            _renderer = GetComponent<Renderer>();
            playerTeam.OnValueChanged += OnTeamChanged;
            state.OnValueChanged += OnStateChanged;
            OnTeamChanged(Team.None, playerTeam.Value);
            OnStateChanged(PlayerState.Alive, state.Value);

            if (IsServer)
            {
                GameManager.Instance.ServerRegisterPlayer(OwnerClientId, this);
                Debug.Log($"[NetworkPlayer] Player spawned: client={OwnerClientId} team={playerTeam.Value} state={state.Value}");
            }
        }

        public override void OnNetworkDespawn()
        {
            playerTeam.OnValueChanged -= OnTeamChanged;
            state.OnValueChanged -= OnStateChanged;
            if (IsServer && GameManager.Instance != null)
            {
                GameManager.Instance.ServerUnregisterPlayer(OwnerClientId);
            }
        }

        private void OnTeamChanged(Team oldTeam, Team newTeam)
        {
            if (_renderer == null) return;
            switch (newTeam)
            {
                case Team.Red: _renderer.material.color = ColorRed; break;
                case Team.Blue: _renderer.material.color = ColorBlue; break;
                default: _renderer.material.color = Color.white; break;
            }
        }

        private void OnStateChanged(PlayerState oldState, PlayerState newState)
        {
            if (_renderer == null) return;
            if (newState == PlayerState.Eliminated)
            {
                _renderer.material.color = ColorEliminated;
            }
            else
            {
                OnTeamChanged(Team.None, playerTeam.Value);
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
            if (kb.spaceKey.wasPressedThisFrame)
            {
                ServerRequestThrow();
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
                return;
            }

            // No core nearby: try the shared orb instead.
            _Project.Scripts.Orb.SharedOrb nearestOrb = FindNearestAvailableOrb(nearestAvailableCoreMaxDistance);
            if (nearestOrb != null)
            {
                nearestOrb.PickupRequestServerRpc();
            }
        }

        private _Project.Scripts.Orb.SharedOrb FindNearestAvailableOrb(float maxDistance)
        {
            _Project.Scripts.Orb.SharedOrb best = null;
            float bestDist = maxDistance;
            foreach (_Project.Scripts.Orb.SharedOrb orb in _Project.Scripts.Orb.SharedOrb.All)
            {
                if (orb.state.Value != _Project.Scripts.Orb.OrbState.Available) continue;
                float dist = Vector3.Distance(transform.position, orb.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = orb;
                }
            }
            return best;
        }

        private void ServerRequestThrow()
        {
            if (!hasOrb.Value)
            {
                return;
            }

            // Client-side aim heuristic (the server validates possession/state and
            // resolves the actual hit): prefer the nearest living enemy.
            Vector2 direction = FindDirectionTowardsNearestEnemy();
            if (direction == Vector2.zero)
            {
                direction = GetComponent<PlayerMovement>().LastMoveDirection;
            }
            if (direction == Vector2.zero)
            {
                direction = Vector2.right;
            }
            _Project.Scripts.Orb.SharedOrb orb = FindHeldOrb();
            if (orb != null)
            {
                orb.ThrowRequestServerRpc(direction);
            }
        }

        private Vector2 FindDirectionTowardsNearestEnemy()
        {
            Vector3 best = Vector3.zero;
            float bestDist = float.MaxValue;
            foreach (NetworkPlayer other in All)
            {
                if (other == this || other.state.Value != PlayerState.Alive) continue;
                if (other.playerTeam.Value == playerTeam.Value || other.playerTeam.Value == Team.None) continue;
                float dist = Vector3.Distance(transform.position, other.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = other.transform.position - transform.position;
                }
            }
            return best.sqrMagnitude > 0.001f ? best.normalized : Vector3.zero;
        }

        private _Project.Scripts.Orb.SharedOrb FindHeldOrb()
        {
            foreach (_Project.Scripts.Orb.SharedOrb orb in _Project.Scripts.Orb.SharedOrb.All)
            {
                if (orb.carrierClientId.Value == OwnerClientId)
                {
                    return orb;
                }
            }
            return null;
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
