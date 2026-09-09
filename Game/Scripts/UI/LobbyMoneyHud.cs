using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local screen-space display for the replicated lobby balance.</summary>
public class LobbyMoneyHud : MonoBehaviour
{
    private static LobbyMoneyHud _instance;

    private LobbyMoney _wallet;
    private TextMeshProUGUI _balanceLabel;

    public static void ShowFor(LobbyMoney wallet)
    {
        if (wallet == null) return;

        CreateIfNeeded();
        _instance.Bind(wallet);
    }

    public static void ShowPreview(int balance)
    {
        CreateIfNeeded();

        if (_instance._wallet != null) _instance._wallet.BalanceChanged -= _instance.UpdateBalance;
        _instance._wallet = null;
        _instance.UpdateBalance(balance);
    }

    private static void CreateIfNeeded()
    {
        if (_instance != null) return;

        GameObject hudObject = new GameObject("Lobby Money HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        _instance = hudObject.AddComponent<LobbyMoneyHud>();
        _instance.CreateVisuals();
        DontDestroyOnLoad(hudObject);
    }

    private void Bind(LobbyMoney wallet)
    {
        if (_wallet == wallet) return;

        if (_wallet != null) _wallet.BalanceChanged -= UpdateBalance;
        _wallet = wallet;
        _wallet.BalanceChanged += UpdateBalance;
        UpdateBalance(_wallet.Balance.Value);
    }

    private void CreateVisuals()
    {
        Canvas canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = new GameObject("Money Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(transform, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(24f, -24f);
        panelRect.sizeDelta = new Vector2(260f, 58f);
        panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        GameObject labelObject = new GameObject("Balance Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(panelObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(16f, 0f);
        labelRect.offsetMax = new Vector2(-16f, 0f);

        _balanceLabel = labelObject.GetComponent<TextMeshProUGUI>();
        _balanceLabel.font = TMP_Settings.defaultFontAsset;
        _balanceLabel.fontSize = 28f;
        _balanceLabel.fontStyle = FontStyles.Bold;
        _balanceLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _balanceLabel.color = new Color(1f, 0.82f, 0.2f);
        _balanceLabel.raycastTarget = false;
    }

    private void UpdateBalance(int balance)
    {
        if (_balanceLabel != null) _balanceLabel.text = $"Gold: {balance:N0}";
    }

    private void OnDestroy()
    {
        if (_wallet != null) _wallet.BalanceChanged -= UpdateBalance;
        if (_instance == this) _instance = null;
    }
}
