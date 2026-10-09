using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace _Project.Scripts.Player
{
    /// <summary>
    /// Mirrors the replicated possession state of a <see cref="NetworkPlayer"/> as a
    /// world-space marker above the avatar, so every client can see who carries the
    /// Core and/or the Orb. Purely presentational: it never changes gameplay state.
    /// </summary>
    public class PlayerPossessionIndicator : NetworkBehaviour
    {
        [SerializeField] private Vector3 markerOffset = new Vector3(0f, 2.2f, 0f);
        [SerializeField] private float bothMarkersSpacing = 0.3f;
        [SerializeField] private float spinDegreesPerSecond = 90f;
        [SerializeField] private Transform coreMarker;
        [SerializeField] private Transform orbMarker;

        private NetworkPlayer _player;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            if (_player != null)
            {
                _player.carriedCoreId.OnValueChanged += OnCarriedCoreChanged;
                _player.hasOrb.OnValueChanged += OnHasOrbChanged;
                _player.state.OnValueChanged += OnStateChanged;
            }

            Refresh();
        }

        public override void OnNetworkDespawn()
        {
            if (_player != null)
            {
                _player.carriedCoreId.OnValueChanged -= OnCarriedCoreChanged;
                _player.hasOrb.OnValueChanged -= OnHasOrbChanged;
                _player.state.OnValueChanged -= OnStateChanged;
            }
        }

        private void OnCarriedCoreChanged(ulong previous, ulong current) => Refresh();
        private void OnHasOrbChanged(bool previous, bool current) => Refresh();
        private void OnStateChanged(PlayerState previous, PlayerState current) => Refresh();

        private void Update()
        {
            if (spinDegreesPerSecond == 0f) return;

            if (coreMarker != null && coreMarker.gameObject.activeSelf)
            {
                coreMarker.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
            }

            if (orbMarker != null && orbMarker.gameObject.activeSelf)
            {
                orbMarker.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
            }
        }

        private void Refresh()
        {
            bool alive = _player == null || _player.state.Value != PlayerState.Eliminated;
            bool showCore = alive && _player != null && _player.carriedCoreId.Value != 0;
            bool showOrb = alive && _player != null && _player.hasOrb.Value;

            if (showCore && showOrb)
            {
                Vector3 left = markerOffset;
                left.x -= bothMarkersSpacing * 0.5f;
                Vector3 right = markerOffset;
                right.x += bothMarkersSpacing * 0.5f;
                coreMarker.localPosition = left;
                orbMarker.localPosition = right;
            }
            else
            {
                coreMarker.localPosition = markerOffset;
                orbMarker.localPosition = markerOffset;
            }

            coreMarker.gameObject.SetActive(showCore);
            orbMarker.gameObject.SetActive(showOrb);
        }
    }
}