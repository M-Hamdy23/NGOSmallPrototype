using _Project.Scripts.Game;
using _Project.Scripts.Player;
using Unity.Netcode;
using UnityEngine;

namespace _Project.Scripts.UI
{
    public class GameHUD : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        private void OnGUI()
        {
            var nw = NetworkManager.Singleton;
            GUILayout.BeginArea(new Rect(340, 10, 380, 200));
            GUILayout.Label("-- Game HUD --");

            if (nw == null || !nw.IsListening)
            {
                GUILayout.Label("Server: not running");
                GUILayout.EndArea();
                return;
            }
            
            if (gameManager != null)
            {
                GUILayout.Label($"Match: { gameManager.matchStateNv.Value} | Red {gameManager.redScore.Value} : {gameManager.blueScore.Value} Blue");
            }
            GUILayout.Label($"Clients: {nw.ConnectedClientsList.Count}");

            NetworkClient localClient = nw.LocalClient;
            var localPlayer = localClient != null ? localClient.PlayerObject : null;
            if (localPlayer != null)
            {
                var player = localPlayer.GetComponent<NetworkPlayer>();
                if (player != null)
                {
                    GUILayout.Label($"You: Team={player.playerTeam.Value} State={player.state.Value} CarryingCore={player.carriedCoreId.Value != 0}");
                }
            }

            GUILayout.Label($"Controls: WASD move | E pickup/interact");
            GUILayout.EndArea();
        }
    }
}

