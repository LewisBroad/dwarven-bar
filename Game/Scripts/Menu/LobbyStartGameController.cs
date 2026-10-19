using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Left-side lobby UI with Steam/voice-ready placeholder player cards.</summary>
[RequireComponent(typeof(NetworkObject))]
public class LobbyStartGameController : NetworkBehaviour
{
    [Header("Game")]
    [SerializeField] private string gameplaySceneName = "SampleScene";
    [SerializeField] private LobbyPresentationController lobbyPresentation;

    [Header("UI")]
    [SerializeField] private bool buildDefaultUi = true;
    [SerializeField] private Sprite placeholderProfilePicture;
    [SerializeField] private Button inviteButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private TextMeshProUGUI statusLabel;

    private readonly List<MemberRow> _rows = new();
    private GameObject _runtimeUi;

    private void Start()
    {
        if (lobbyPresentation == null) lobbyPresentation = GetComponent<LobbyPresentationController>();
        if (lobbyPresentation == null) lobbyPresentation = FindObjectOfType<LobbyPresentationController>();
        if (lobbyPresentation != null) lobbyPresentation.PlayerCountChanged += RefreshMemberRows;
        if (buildDefaultUi && startGameButton == null) BuildDefaultUi();
        RefreshUi();
    }

    public override void OnNetworkSpawn() => RefreshUi();

    public override void OnNetworkDespawn()
    {
        if (lobbyPresentation != null) lobbyPresentation.PlayerCountChanged -= RefreshMemberRows;
        base.OnNetworkDespawn();
    }

    public void RequestStartGame()
    {
        if (!IsSpawned)
        {
            SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
            return;
        }
        if (!IsServer)
        {
            SetStatus("Waiting for the host to start the game...");
            return;
        }
        if (NetworkManager == null)
        {
            SetStatus("Network Manager is unavailable.");
            return;
        }
        if (NetworkManager.NetworkConfig.EnableSceneManagement)
            NetworkManager.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        else
            SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
    }

    /// <summary>Replace this with the Steam invite overlay once Steam is integrated.</summary>
    public void InviteFriends() => SetStatus("Steam invites will appear here once Steam is connected.");

    /// <summary>Call from voice chat integration to control a slot's speaker colour.</summary>
    public void SetPlayerTalking(int slot, bool isTalking)
    {
        if (slot < 0 || slot >= _rows.Count) return;
        _rows[slot].speaker.color = isTalking ? new Color(0.35f, 1f, 0.5f) : new Color(0.56f, 0.45f, 0.32f);
    }

    /// <summary>Call from Steam integration to replace placeholder identities.</summary>
    public void SetPlayerIdentity(int slot, string username, Sprite portrait = null)
    {
        if (slot < 0 || slot >= _rows.Count) return;
        _rows[slot].username.text = username;
        if (portrait != null) _rows[slot].portrait.sprite = portrait;
    }

    private void RefreshUi()
    {
        bool hostCanStart = !IsSpawned || IsServer;
        if (startGameButton != null) startGameButton.gameObject.SetActive(hostCanStart);
        if (inviteButton != null) inviteButton.gameObject.SetActive(hostCanStart);
        RefreshMemberRows(lobbyPresentation != null ? lobbyPresentation.PlayerCount : (!IsSpawned ? 4 : 1));
        SetStatus(hostCanStart ? "Invite friends, then start when ready." : "Waiting for the host to start the game...");
    }

    private void RefreshMemberRows(int playerCount)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            bool active = i < playerCount;
            _rows[i].root.SetActive(active);
            if (!active) continue;
            _rows[i].username.text = i == 0 ? "DwarvenHost" : $"Dwarf Guest {i}";
            _rows[i].role.text = i == 0 ? "HOST" : "JOINED";
            SetPlayerTalking(i, false);
        }
    }

    private void BuildDefaultUi()
    {
        EnsureEventSystem();
        _runtimeUi = new GameObject("Lobby Screen UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = _runtimeUi.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = _runtimeUi.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        Image panel = CreateImage(_runtimeUi.transform, "Lobby Panel", new Color(0.105f, 0.055f, 0.024f, 0.94f));
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(28f, 0f);
        panelRect.sizeDelta = new Vector2(445f, 820f);

        CreateText(panel.transform, "TAVERN LOBBY", 35f, new Color(1f, 0.76f, 0.32f), new Vector2(0f, 365f), new Vector2(400f, 56f), FontStyles.Bold);
        CreateText(panel.transform, "UP TO FOUR DWARVES", 16f, new Color(0.85f, 0.71f, 0.5f), new Vector2(0f, 325f), new Vector2(400f, 30f), FontStyles.Normal);
        for (int i = 0; i < 4; i++) _rows.Add(CreateMemberRow(panel.transform, i));
        inviteButton = CreateButton(panel.transform, "Invite Friends", new Vector2(-100f, -335f), new Vector2(190f, 58f), InviteFriends, new Color(0.24f, 0.14f, 0.07f));
        startGameButton = CreateButton(panel.transform, "Start Game", new Vector2(105f, -335f), new Vector2(190f, 58f), RequestStartGame, new Color(0.42f, 0.22f, 0.07f));
        statusLabel = CreateText(panel.transform, "", 16f, new Color(0.86f, 0.75f, 0.56f), new Vector2(0f, -385f), new Vector2(400f, 34f), FontStyles.Normal);
    }

    private MemberRow CreateMemberRow(Transform parent, int slot)
    {
        Image row = CreateImage(parent, $"Player {slot + 1}", new Color(0.19f, 0.1f, 0.045f, 0.96f));
        RectTransform rowRect = row.rectTransform;
        rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = new Vector2(0f, 235f - slot * 110f);
        rowRect.sizeDelta = new Vector2(395f, 92f);

        Image portrait = CreateImage(row.transform, "Profile Placeholder", new Color(0.63f, 0.4f, 0.17f));
        RectTransform portraitRect = portrait.rectTransform;
        portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(0f, 0.5f);
        portraitRect.pivot = new Vector2(0f, 0.5f);
        portraitRect.anchoredPosition = new Vector2(12f, 0f);
        portraitRect.sizeDelta = new Vector2(66f, 66f);
        if (placeholderProfilePicture != null) portrait.sprite = placeholderProfilePicture;
        CreateText(portrait.transform, "?", 35f, Color.white, Vector2.zero, new Vector2(60f, 60f), FontStyles.Bold);

        TextMeshProUGUI username = CreateText(row.transform, "", 24f, Color.white, new Vector2(-42f, 16f), new Vector2(250f, 35f), FontStyles.Bold);
        username.alignment = TextAlignmentOptions.MidlineLeft;
        TextMeshProUGUI role = CreateText(row.transform, "", 14f, new Color(1f, 0.75f, 0.3f), new Vector2(-42f, -18f), new Vector2(250f, 25f), FontStyles.Bold);
        role.alignment = TextAlignmentOptions.MidlineLeft;
        TextMeshProUGUI speaker = CreateText(row.transform, "🔊", 26f, new Color(0.56f, 0.45f, 0.32f), new Vector2(160f, 0f), new Vector2(50f, 55f), FontStyles.Normal);
        return new MemberRow(row.gameObject, portrait, username, role, speaker);
    }

    private Button CreateButton(Transform parent, string text, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action, Color color)
    {
        Image image = CreateImage(parent, text + " Button", color);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        CreateText(image.transform, text, 20f, Color.white, Vector2.zero, size - new Vector2(12f, 8f), FontStyles.Bold);
        return button;
    }

    private static Image CreateImage(Transform parent, string name, Color color)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string text, float fontSize, Color color, Vector2 position, Vector2 size, FontStyles style)
    {
        GameObject go = new("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }

    private void SetStatus(string value)
    {
        if (statusLabel != null) statusLabel.text = value;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        GameObject eventSystem = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(eventSystem);
    }

    public override void OnDestroy()
    {
        if (lobbyPresentation != null) lobbyPresentation.PlayerCountChanged -= RefreshMemberRows;
        if (_runtimeUi != null) Destroy(_runtimeUi);
        base.OnDestroy();
    }

    private sealed class MemberRow
    {
        public readonly GameObject root;
        public readonly Image portrait;
        public readonly TextMeshProUGUI username;
        public readonly TextMeshProUGUI role;
        public readonly TextMeshProUGUI speaker;

        public MemberRow(GameObject root, Image portrait, TextMeshProUGUI username, TextMeshProUGUI role, TextMeshProUGUI speaker)
        {
            this.root = root;
            this.portrait = portrait;
            this.username = username;
            this.role = role;
            this.speaker = speaker;
        }
    }
}
