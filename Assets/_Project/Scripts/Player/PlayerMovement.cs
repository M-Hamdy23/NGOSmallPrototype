using _Project.Scripts.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _Project.Scripts.Player
{
    public class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float carrierSpeedMultiplier = 0.5f; // plan §7: orb carrier moves at 50%
        [SerializeField] private Vector2 arenaHalfExtents = new Vector2(29f, 14.5f);
        [SerializeField] private float inputSendRate = 20f;
        [SerializeField] private InputActionReference moveActionReference;
        private Vector2 _serverInput;
        private Vector2 _lastSentInput;
        private Vector2 _externalInput;
        private Vector2 _lastMoveDirection = Vector2.right;
        private float _nextSendTime;

        private NetworkPlayer _networkPlayer;

        public Vector2 LastMoveDirection => _lastMoveDirection;

        public override void OnNetworkSpawn()
        {
            name = "Player_" + OwnerClientId;
            _networkPlayer = GetComponent<NetworkPlayer>();
            if (IsOwner)
            {
                EnableInputActions();
            }
        }

        public override void OnNetworkDespawn()
        {
            DisableInputActions();
        }

        private void EnableInputActions()
        {
            if (moveActionReference) moveActionReference.action.Enable();
        }

        private void DisableInputActions()
        {
            if (moveActionReference) moveActionReference.action.Disable();
        }

        private void Update()
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                if (IsOwner)
                {
                    _serverInput = ReadLocalInput();
                }

                ApplyServerMovement();
            }
            else if (IsOwner)
            {
                SendInputWhenChanged();
            }
        }

        private Vector2 ReadLocalInput()
        {
            if (_externalInput != Vector2.zero)
            {
                return Vector2.ClampMagnitude(_externalInput, 1f);
            }

            // Input System action (Player/Move) — driven by WASD composite,
            // gamepad leftStick and the on-screen joystick (OnScreenStick).
            if (moveActionReference != null)
            {
                return moveActionReference.action.ReadValue<Vector2>();
            }

            return Vector2.zero;
        }

        public void SetExternalInput(Vector2 input)
        {
            _externalInput = input;
        }

        private void SendInputWhenChanged()
        {
            if (Time.time < _nextSendTime) return;
            Vector2 input = ReadLocalInput();
            if (input == _lastSentInput) return;
            _lastSentInput = input;
            if (input != Vector2.zero)
            {
                _lastMoveDirection = input;
            }

            _nextSendTime = Time.time + 1f / inputSendRate;
            MoveInputServerRpc(input);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void MoveInputServerRpc(Vector2 input)
        {
            if (!IsServer) return;
            // Server validation: eliminated players cannot move (plan §10).
            if (_networkPlayer != null && _networkPlayer.state.Value != PlayerState.Alive)
            {
                _serverInput = Vector2.zero;
                return;
            }

            _serverInput = Vector2.ClampMagnitude(input, 1f);
        }

        private void ApplyServerMovement()
        {
            float speed = GetServerSpeed();
            if (_serverInput != Vector2.zero)
            {
                _lastMoveDirection = _serverInput;
            }

            Vector3 delta = new Vector3(_serverInput.x, 0f, _serverInput.y) * (speed * Time.deltaTime);
            Vector3 pos = transform.position + delta;
            pos.x = Mathf.Clamp(pos.x, -arenaHalfExtents.x, arenaHalfExtents.x);
            pos.z = Mathf.Clamp(pos.z, -arenaHalfExtents.y, arenaHalfExtents.y);
            transform.position = pos;
        }

        private float GetServerSpeed()
        {
            if (_networkPlayer == null) return moveSpeed;
            // Gameplay frozen unless the match is Playing (plan §11).
            var gm = _Project.Scripts.Game.GameManager.Instance;
            if (gm == null || gm.matchStateNv.Value != MatchState.Playing) return 0f;
            if (_networkPlayer.state.Value != PlayerState.Alive) return 0f;
            return _networkPlayer.hasOrb.Value ? moveSpeed * carrierSpeedMultiplier : moveSpeed;
        }
    }
}