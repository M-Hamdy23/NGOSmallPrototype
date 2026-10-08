using _Project.Scripts.Game;
using _Project.Scripts.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace _Project.Scripts.UI
{
    /// <summary>
    /// Mobile, Canvas-based replacement for the old IMGUI game HUD.
    /// Shows a top-centre scoreboard, a match-state banner and a compact
    /// session/player panel. Hidden until a network session is running.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("Binding")] [SerializeField] private GameManager gameManager;

        [Header("Root")] [SerializeField] private GameObject hudRoot;

        [Header("Scoreboard")] [SerializeField]
        private Text redScoreText;

        [SerializeField] private Text blueScoreText;

        [Header("Match State Banner")] [SerializeField]
        private GameObject stateBanner;

        [SerializeField] private Image stateBannerImage;
        [SerializeField] private Text stateBannerText;

        [Header("Session / Player")] [SerializeField]
        private Text clientsText;

        [SerializeField] private Text playerText;

        private static readonly Color WaitingColor = new Color(0.55f, 0.45f, 0.15f, 0.94f);
        private static readonly Color StartingColor = new Color(0.15f, 0.45f, 0.75f, 0.94f);
        private static readonly Color FinishedColor = new Color(0.16f, 0.58f, 0.34f, 0.94f);

        private void Awake()
        {
            if (gameManager == null)
            {
                gameManager = FindAnyObjectByType<GameManager>();
            }
        }

        private void Update()
        {
            NetworkManager nw = NetworkManager.Singleton;
            bool listening = nw != null && nw.IsListening;

            if (hudRoot != null) hudRoot.SetActive(listening);
            if (!listening) return;

            if (clientsText != null)
            {
                clientsText.text = "CLIENTS   " + nw.ConnectedClientsList.Count;
            }

            if (gameManager != null && gameManager.IsSpawned)
            {
                if (redScoreText != null) redScoreText.text = gameManager.redScore.Value.ToString();
                if (blueScoreText != null) blueScoreText.text = gameManager.blueScore.Value.ToString();
                RefreshMatchState(
                    gameManager.matchStateNv.Value,
                    gameManager.startingCountdown.Value,
                    gameManager.winningTeam.Value);
            }
            else
            {
                if (redScoreText != null) redScoreText.text = "0";
                if (blueScoreText != null) blueScoreText.text = "0";
                SetBanner(false, null, Color.clear);
            }

            RefreshLocalPlayer(nw);
        }

        private void RefreshMatchState(MatchState state, int countdown, Team winner)
        {
            switch (state)
            {
                case MatchState.WaitingForPlayers:
                    SetBanner(true, $"WAITING FOR PLAYERS  ({gameManager.MinimumPlayersToStart} REQUIRED)", WaitingColor);
                    break;

                case MatchState.Starting:
                    SetBanner(true, "MATCH STARTING  " + Mathf.Max(0, countdown), StartingColor);
                    break;

                case MatchState.Finished:
                    string winnerName = winner == Team.Red ? "RED TEAM WINS"
                        : winner == Team.Blue ? "BLUE TEAM WINS"
                        : "MATCH OVER";
                    SetBanner(true, winnerName, FinishedColor);
                    break;

                default:
                    SetBanner(false, null, Color.clear);
                    break;
            }
        }

        private void SetBanner(bool show, string message, Color color)
        {
            if (stateBanner != null) stateBanner.SetActive(show);
            if (!show) return;
            if (stateBannerText != null) stateBannerText.text = message;
            if (stateBannerImage != null) stateBannerImage.color = color;
        }

        private void RefreshLocalPlayer(NetworkManager nw)
        {
            if (playerText == null) return;

            NetworkClient local = nw.LocalClient;
            NetworkObject playerObject = local != null ? local.PlayerObject : null;
            NetworkPlayer player = playerObject != null ? playerObject.GetComponent<NetworkPlayer>() : null;

            if (player == null)
            {
                playerText.text = "SPECTATING";
                return;
            }

            string team = player.playerTeam.Value.ToString();
            string state = player.state.Value.ToString();
            string core = player.carriedCoreId.Value != 0 ? "Yes" : "No";
            string orb = player.hasOrb.Value ? "Yes" : "No";
            playerText.text = $"{team}  |  {state}  |  Core: {core}  |  Orb: {orb}";
        }
    }
}