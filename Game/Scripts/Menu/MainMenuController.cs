using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The front-end menu. It can build its own simple UI for a fresh menu scene,
/// or use inspector-assigned buttons/panels in a polished, authored menu.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Networking")]
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private ushort port = 7777;
    [SerializeField] private string defaultJoinAddress = "127.0.0.1";

    [Header("Optional authored UI")]
    [SerializeField] private Canvas menuCanvas;
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject joinPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private TMP_InputField addressInput;
    [SerializeField] private TextMeshProUGUI statusLabel;

    [Header("Fallback UI")]
    [SerializeField] private bool buildDefaultUi = true;

    private readonly Color _paper = new(0.16f, 0.09f, 0.04f, 0.96f);
    private readonly Color _button = new(0.37f, 0.19f, 0.07f, 1f);
    private readonly Color _gold = new(1f, 0.72f, 0.25f, 1f);

    private void Awake()
    {
        if (menuCanvas == null && buildDefaultUi) BuildDefaultUi();
        SetPanel(joinPanel, false);
        SetPanel(settingsPanel, false);
        ShowCursor(true);
    }

    public void HostGame()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            SetStatus("Add a NetworkManager with Unity Transport to the Menu scene first.");
            return;
        }

        if (!manager.IsListening && !manager.StartHost())
        {
            SetStatus("Could not start the host.");
            return;
        }

        SetStatus("Opening lobby...");
        LoadLobby(manager);
    }

    public void ShowJoin()
    {
        SetPanel(mainPanel, false);
        SetPanel(settingsPanel, false);
        SetPanel(joinPanel, true);
        if (addressInput != null)
        {
            if (string.IsNullOrWhiteSpace(addressInput.text)) addressInput.text = defaultJoinAddress;
            addressInput.ActivateInputField();
        }
    }

    public void JoinGame()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            SetStatus("Add a NetworkManager with Unity Transport to the Menu scene first.");
            return;
        }

        UnityTransport transport = manager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            SetStatus("The NetworkManager needs a Unity Transport component.");
            return;
        }

        string address = addressInput != null && !string.IsNullOrWhiteSpace(addressInput.text)
            ? addressInput.text.Trim()
            : defaultJoinAddress;
        transport.SetConnectionData(address, port);
        if (!manager.IsListening && !manager.StartClient())
        {
            SetStatus("Could not connect. Check the address and port.");
            return;
        }

        SetStatus($"Connecting to {address}:{port}...");
    }

    public void ShowSettings()
    {
        SetPanel(mainPanel, false);
        SetPanel(joinPanel, false);
        SetPanel(settingsPanel, true);
    }

    public void BackToMain()
    {
        SetPanel(joinPanel, false);
        SetPanel(settingsPanel, false);
        SetPanel(mainPanel, true);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void LoadLobby(NetworkManager manager)
    {
        if (manager.NetworkConfig.EnableSceneManagement)
            manager.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        else
            SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
    }

    private void BuildDefaultUi()
    {
        EnsureEventSystem();
        GameObject root = new("Main Menu UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        menuCanvas = root.GetComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        menuCanvas.sortingOrder = 1000;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        Image backdrop = CreateImage(root.transform, "Backdrop", new Color(0.025f, 0.015f, 0.01f, 0.92f));
        Stretch(backdrop.rectTransform);
        mainPanel = CreatePanel(root.transform, "Main Panel", new Vector2(0.5f, 0.5f), new Vector2(560f, 620f));
        AddText(mainPanel.transform, "Dwarf Bar", 64, _gold, new Vector2(0f, 210f), new Vector2(500f, 90f));
        AddText(mainPanel.transform, "A tavern worth fighting for", 24, new Color(1f, 0.88f, 0.66f), new Vector2(0f, 145f), new Vector2(500f, 44f));
        AddButton(mainPanel.transform, "Host Game", new Vector2(0f, 55f), HostGame);
        AddButton(mainPanel.transform, "Join Game", new Vector2(0f, -30f), ShowJoin);
        AddButton(mainPanel.transform, "Settings", new Vector2(0f, -115f), ShowSettings);
        AddButton(mainPanel.transform, "Quit", new Vector2(0f, -200f), QuitGame);

        joinPanel = CreatePanel(root.transform, "Join Panel", new Vector2(0.5f, 0.5f), new Vector2(560f, 380f));
        AddText(joinPanel.transform, "Join Game", 48, _gold, new Vector2(0f, 115f), new Vector2(480f, 70f));
        addressInput = AddInput(joinPanel.transform, new Vector2(0f, 30f));
        addressInput.text = defaultJoinAddress;
        AddButton(joinPanel.transform, "Connect", new Vector2(0f, -80f), JoinGame);
        AddButton(joinPanel.transform, "Back", new Vector2(0f, -165f), BackToMain);

        settingsPanel = CreatePanel(root.transform, "Settings Panel", new Vector2(0.5f, 0.5f), new Vector2(560f, 330f));
        AddText(settingsPanel.transform, "Settings", 48, _gold, new Vector2(0f, 90f), new Vector2(480f, 70f));
        AddText(settingsPanel.transform, "Settings controls can be added here as the game grows.", 22, Color.white, new Vector2(0f, 12f), new Vector2(470f, 55f));
        AddButton(settingsPanel.transform, "Back", new Vector2(0f, -105f), BackToMain);

        statusLabel = AddText(root.transform, "", 20, new Color(1f, 0.8f, 0.45f), new Vector2(0f, -470f), new Vector2(1100f, 50f));
    }

    private GameObject CreatePanel(Transform parent, string name, Vector2 anchor, Vector2 size)
    {
        Image panel = CreateImage(parent, name, _paper);
        RectTransform rect = panel.rectTransform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        return panel.gameObject;
    }

    private Image CreateImage(Transform parent, string name, Color color)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private TextMeshProUGUI AddText(Transform parent, string text, float size, Color color, Vector2 position, Vector2 dimensions)
    {
        GameObject go = new("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = dimensions;
        rect.anchoredPosition = position;
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }

    private void AddButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        Image image = CreateImage(parent, label + " Button", _button);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(390f, 64f);
        rect.anchoredPosition = position;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        AddText(image.transform, label, 28, Color.white, Vector2.zero, new Vector2(360f, 56f));
    }

    private TMP_InputField AddInput(Transform parent, Vector2 position)
    {
        Image background = CreateImage(parent, "Server Address", Color.white);
        RectTransform rect = background.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(390f, 58f);
        rect.anchoredPosition = position;
        TMP_InputField input = background.gameObject.AddComponent<TMP_InputField>();
        TextMeshProUGUI text = AddText(background.transform, "", 24, Color.black, Vector2.zero, new Vector2(355f, 50f));
        text.alignment = TextAlignmentOptions.MidlineLeft;
        TextMeshProUGUI placeholder = AddText(background.transform, "Server address", 24, new Color(0f, 0f, 0f, 0.4f), Vector2.zero, new Vector2(355f, 50f));
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        input.textComponent = text;
        input.placeholder = placeholder;
        return input;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        GameObject eventSystem = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(eventSystem);
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null) statusLabel.text = message;
    }

    private static void SetPanel(GameObject panel, bool active)
    {
        if (panel != null) panel.SetActive(active);
    }

    private static void ShowCursor(bool visible)
    {
        Cursor.visible = visible;
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
    }
}
