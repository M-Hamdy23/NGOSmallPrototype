using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _Project.Scripts.Player
{
    public class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private Vector2 arenaHalfExtents = new Vector2(29f, 14.5f);
        [SerializeField] private float inputSendRate = 20f;

        private Vector2 _serverInput;
        private Vector2 _lastSentInput;
        private Vector2 _externalInput;
        private float _nextSendTime;

        public override void OnNetworkSpawn()
        {
            name = "Player_" + OwnerClientId;
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
            Keyboard kb = Keyboard.current;
            if (kb == null) return Vector2.zero;

            Vector2 input = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
            return Vector2.ClampMagnitude(input, 1f);
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
            _nextSendTime = Time.time + 1f / inputSendRate;
            MoveInputServerRpc(input);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void MoveInputServerRpc(Vector2 input)
        {
            if (!IsServer) return;
            _serverInput = Vector2.ClampMagnitude(input, 1f);
        }

        private void ApplyServerMovement()
        {
            Vector3 delta = new Vector3(_serverInput.x, 0f, _serverInput.y) * (moveSpeed * Time.deltaTime);
            Vector3 pos = transform.position + delta;
            pos.x = Mathf.Clamp(pos.x, -arenaHalfExtents.x, arenaHalfExtents.x);
            pos.z = Mathf.Clamp(pos.z, -arenaHalfExtents.y, arenaHalfExtents.y);
            transform.position = pos;
        }
    }
}
