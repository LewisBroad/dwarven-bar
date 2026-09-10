using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Mouse-driven, two-page world-space shop displayed directly on the magic book.</summary>
public class MagicOrderBookUI : MonoBehaviour
{
    private const int ItemsPerPage = 2;
    private static MagicOrderBookUI _instance;
    public static bool IsOpen => _instance != null && _instance._isOpen;

    public static void ForceClose(bool immediate = false)
    {
        if (_instance != null) _instance.Close(immediate);
    }

    private readonly List<ShopItemDefinition> _items = new();
    private readonly List<BookItemCard> _cards = new();
    private PlayerInputActions _inputActions;
    private ShopOrderManager _manager;
    private Canvas _canvas;
    private GameObject _bookRoot;
    private Transform _leftPage;
    private Transform _rightPage;
    private TextMeshProUGUI _statusLabel;
    private TextMeshProUGUI _pageLabel;
    private Button _previousButton;
    private Button _nextButton;
    private RectTransform _closeButtonRect;
    private TMP_FontAsset _font;
    private bool _isOpen;
    private int _spreadIndex;
    private int _selectedCard = -1;
    private Transform _cameraTransform;
    private Transform _originalCameraParent;
    private Vector3 _originalCameraLocalPosition;
    private Quaternion _originalCameraLocalRotation;
    private Coroutine _cameraTransition;
    private float _cameraTransitionDuration;

    public static void Open(ShopOrderManager manager, MagicShopCatalog catalog, Camera playerCamera, Transform viewAnchor, Transform uiAnchor, float uiScale, TMP_FontAsset font, float cameraTransitionDuration)
    {
        if (_instance == null) Create();
        _instance.Show(manager, catalog, playerCamera, viewAnchor, uiAnchor, uiScale, font, cameraTransitionDuration);
    }

    private static void Create()
    {
        GameObject root = new GameObject("Magic Order Book UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        _instance = root.AddComponent<MagicOrderBookUI>();
        DontDestroyOnLoad(root);
        _instance.BuildVisuals();
    }

    private void Awake() => _inputActions = new PlayerInputActions();
    private void OnDestroy() => _inputActions?.Dispose();

    private void OnEnable()
    {
        _inputActions.Player.Enable();
        _inputActions.Player.Back.performed += OnClose;
    }

    private void OnDisable()
    {
        _inputActions.Player.Back.performed -= OnClose;
        _inputActions.Player.Disable();
    }

    private void Show(ShopOrderManager manager, MagicShopCatalog catalog, Camera playerCamera, Transform viewAnchor, Transform uiAnchor, float uiScale, TMP_FontAsset font, float cameraTransitionDuration)
    {
        _manager = manager;
        _font = font != null ? font : TMP_Settings.defaultFontAsset;
        _items.Clear();
        if (catalog != null) foreach (ShopItemDefinition item in catalog.Items) if (item != null) _items.Add(item);

        _spreadIndex = 0;
        _selectedCard = -1;
        _cameraTransitionDuration = cameraTransitionDuration;
        _isOpen = true;
        _bookRoot.SetActive(true);
        foreach (TextMeshProUGUI label in _bookRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.font = _font;
        }
        FocusBook(playerCamera, viewAnchor);
        PlaceOnBook(playerCamera, uiAnchor, uiScale);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        RebuildSpread();
    }

    private void FocusBook(Camera playerCamera, Transform viewAnchor)
    {
        if (playerCamera == null || viewAnchor == null) return;
        _cameraTransform = playerCamera.transform;
        _originalCameraParent = _cameraTransform.parent;
        _originalCameraLocalPosition = _cameraTransform.localPosition;
        _originalCameraLocalRotation = _cameraTransform.localRotation;
        StartCameraTransition(viewAnchor.position, viewAnchor.rotation);
    }

    private void PlaceOnBook(Camera playerCamera, Transform uiAnchor, float uiScale)
    {
        if (uiAnchor == null) return;
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = playerCamera;
        _canvas.transform.SetParent(uiAnchor, false);
        _canvas.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        _canvas.transform.localScale = Vector3.one * uiScale;
    }

    private void RestoreCamera(bool immediate = false)
    {
        if (_cameraTransform == null) return;

        Vector3 targetPosition = _originalCameraParent != null
            ? _originalCameraParent.TransformPoint(_originalCameraLocalPosition)
            : _originalCameraLocalPosition;
        Quaternion targetRotation = _originalCameraParent != null
            ? _originalCameraParent.rotation * _originalCameraLocalRotation
            : _originalCameraLocalRotation;

        if (_cameraTransition != null) StopCoroutine(_cameraTransition);
        if (immediate)
        {
            _cameraTransform.SetPositionAndRotation(targetPosition, targetRotation);
            _cameraTransform.SetParent(_originalCameraParent, false);
            _cameraTransform.localPosition = _originalCameraLocalPosition;
            _cameraTransform.localRotation = _originalCameraLocalRotation;
            _cameraTransform = null;
            return;
        }
        _cameraTransition = StartCoroutine(ReturnCamera(targetPosition, targetRotation));
    }

    private void StartCameraTransition(Vector3 targetPosition, Quaternion targetRotation)
    {
        if (_cameraTransition != null) StopCoroutine(_cameraTransition);
        _cameraTransition = StartCoroutine(BlendCamera(targetPosition, targetRotation, false));
    }

    private System.Collections.IEnumerator ReturnCamera(Vector3 targetPosition, Quaternion targetRotation)
    {
        yield return BlendCamera(targetPosition, targetRotation, true);
        if (_cameraTransform != null)
        {
            _cameraTransform.SetParent(_originalCameraParent, false);
            _cameraTransform.localPosition = _originalCameraLocalPosition;
            _cameraTransform.localRotation = _originalCameraLocalRotation;
            _cameraTransform = null;
        }
    }

    private System.Collections.IEnumerator BlendCamera(Vector3 targetPosition, Quaternion targetRotation, bool restoreAfterBlend)
    {
        if (_cameraTransform == null) yield break;
        Vector3 startPosition = _cameraTransform.position;
        Quaternion startRotation = _cameraTransform.rotation;

        if (_cameraTransitionDuration <= 0f)
        {
            _cameraTransform.SetPositionAndRotation(targetPosition, targetRotation);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < _cameraTransitionDuration && _cameraTransform != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _cameraTransitionDuration);
            _cameraTransform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, targetPosition, t),
                Quaternion.Slerp(startRotation, targetRotation, t));
            yield return null;
        }

        if (_cameraTransform != null) _cameraTransform.SetPositionAndRotation(targetPosition, targetRotation);
    }

    private void OnClose(InputAction.CallbackContext context) => Close();
    private void Close() => Close(false);

    private void Close(bool immediate)
    {
        if (!_isOpen) return;
        _isOpen = false;
        _bookRoot.SetActive(false);
        RestoreCamera(immediate);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void TurnPage(int direction)
    {
        int spreadCount = Mathf.Max(1, Mathf.CeilToInt(_items.Count / (float)(ItemsPerPage * 2)));
        int next = Mathf.Clamp(_spreadIndex + direction, 0, spreadCount - 1);
        if (next == _spreadIndex) return;
        _spreadIndex = next;
        _selectedCard = -1;
        RebuildSpread();
    }

    private void Purchase(ShopItemDefinition item)
    {
        if (!_isOpen || item == null) return;
        if (_manager == null) { _statusLabel.text = "No order manager is available."; return; }
        _manager.TryOrder(item, out string result);
        _statusLabel.text = result;
        RebuildSpread();
    }

    private void BuildVisuals()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 200;
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        GetComponent<RectTransform>().sizeDelta = new Vector2(1440f, 800f);

        _bookRoot = CreatePanel(transform, "Open Magic Book", Color.clear);
        Stretch(_bookRoot.GetComponent<RectTransform>());
        GameObject spread = CreatePanel(_bookRoot.transform, "Book Spread", new Color(0.39f, 0.25f, 0.11f, 0.98f));
        RectTransform spreadRect = spread.GetComponent<RectTransform>();
        spreadRect.anchorMin = spreadRect.anchorMax = new Vector2(0.5f, 0.5f);
        spreadRect.sizeDelta = new Vector2(1440f, 800f);

        _leftPage = CreatePage(spread.transform, "Left Page", new Vector2(-355f, 0f));
        _rightPage = CreatePage(spread.transform, "Right Page", new Vector2(355f, 0f));
        CreateLabel(spread.transform, "MAGIC ORDERS", 42f, new Vector2(0f, 350f), new Vector2(900f, 60f), TextAlignmentOptions.Center);
        _pageLabel = CreateLabel(spread.transform, "", 20f, new Vector2(0f, -350f), new Vector2(520f, 35f), TextAlignmentOptions.Center);
        _statusLabel = CreateLabel(_bookRoot.transform, "", 19f, new Vector2(0f, -430f), new Vector2(1500f, 40f), TextAlignmentOptions.Center);

        _previousButton = CreateButton(spread.transform, "‹", new Vector2(-650f, -340f), new Vector2(60f, 55f));
        _nextButton = CreateButton(spread.transform, "›", new Vector2(650f, -340f), new Vector2(60f, 55f));
        Button closeButton = CreateButton(spread.transform, "×", new Vector2(675f, 350f), new Vector2(52f, 52f));
        _closeButtonRect = closeButton.GetComponent<RectTransform>();
        _previousButton.onClick.AddListener(() => TurnPage(-1));
        _nextButton.onClick.AddListener(() => TurnPage(1));
        closeButton.onClick.AddListener(Close);
        _bookRoot.SetActive(false);
    }

    private Transform CreatePage(Transform parent, string name, Vector2 position)
    {
        GameObject page = CreatePanel(parent, name, new Color(0.91f, 0.78f, 0.55f, 1f));
        RectTransform rect = page.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(680f, 680f);
        return page.transform;
    }

    private void RebuildSpread()
    {
        ClearChildren(_leftPage);
        ClearChildren(_rightPage);
        _cards.Clear();
        int first = _spreadIndex * ItemsPerPage * 2;
        for (int index = 0; index < ItemsPerPage * 2; index++)
        {
            if (first + index >= _items.Count) break;
            Transform page = index < ItemsPerPage ? _leftPage : _rightPage;
            float y = index % ItemsPerPage == 0 ? 155f : -165f;
            _cards.Add(CreateItemCard(page, _items[first + index], new Vector2(0f, y), index == _selectedCard));
        }

        int spreadCount = Mathf.Max(1, Mathf.CeilToInt(_items.Count / (float)(ItemsPerPage * 2)));
        _pageLabel.text = $"Pages {_spreadIndex * 2 + 1}–{Mathf.Min(_spreadIndex * 2 + 2, spreadCount * 2)}";
        _previousButton.interactable = _spreadIndex > 0;
        _nextButton.interactable = _spreadIndex < spreadCount - 1;
        if (_items.Count == 0) _statusLabel.text = "This book has no available orders.";
        else if (string.IsNullOrEmpty(_statusLabel.text)) _statusLabel.text = "Select an item to place an order.";
    }

    private BookItemCard CreateItemCard(Transform page, ShopItemDefinition item, Vector2 position, bool selected)
    {
        GameObject card = CreatePanel(page, "Order: " + item.DisplayName, selected ? new Color(0.55f, 0.34f, 0.12f) : new Color(0.73f, 0.57f, 0.33f));
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(620f, 280f);

        Image icon = new GameObject("Item Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        icon.transform.SetParent(card.transform, false);
        RectTransform iconRect = icon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(25f, 0f);
        iconRect.sizeDelta = new Vector2(180f, 180f);
        icon.sprite = item.Icon;
        icon.color = item.Icon == null ? new Color(0.2f, 0.13f, 0.06f) : Color.white;
        icon.preserveAspect = true;

        CreateLabel(card.transform, item.DisplayName, 27f, new Vector2(135f, 94f), new Vector2(320f, 55f), TextAlignmentOptions.MidlineLeft);
        CreateLabel(card.transform, item.Description, 19f, new Vector2(135f, 0f), new Vector2(320f, 125f), TextAlignmentOptions.TopLeft);
        TextMeshProUGUI price = CreateLabel(card.transform, $"{item.Price} Gold", 24f, new Vector2(135f, -96f), new Vector2(320f, 48f), TextAlignmentOptions.BottomRight);
        price.fontStyle = FontStyles.Bold;
        price.color = new Color(1f, 0.81f, 0.2f);
        return new BookItemCard(item, rect);
    }

    private void Update()
    {
        if (!_isOpen || Mouse.current == null) return;
        Vector2 pointer = Mouse.current.position.ReadValue();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (RectangleContains(_closeButtonRect, pointer)) { Close(); return; }
            if (RectangleContains(_previousButton.transform as RectTransform, pointer)) { TurnPage(-1); return; }
            if (RectangleContains(_nextButton.transform as RectTransform, pointer)) { TurnPage(1); return; }
        }

        for (int index = 0; index < _cards.Count; index++)
        {
            if (!RectangleContains(_cards[index].Rect, pointer)) continue;
            if (_selectedCard != index) { _selectedCard = index; RebuildSpread(); }
            if (Mouse.current.leftButton.wasPressedThisFrame) Purchase(_cards[index].Item);
            break;
        }
    }

    private bool RectangleContains(RectTransform rect, Vector2 pointer)
    {
        return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, _canvas.worldCamera);
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string text, float size, Vector2 position, Vector2 dimensions, TextAlignmentOptions alignment)
    {
        GameObject label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(parent, false);
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = true;
        return tmp;
    }

    private static Button CreateButton(Transform parent, string text, Vector2 position, Vector2 dimensions)
    {
        GameObject button = new GameObject("Book Button", typeof(RectTransform), typeof(Image), typeof(Button));
        button.transform.SetParent(parent, false);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        button.GetComponent<Image>().color = new Color(0.25f, 0.13f, 0.04f, 0.9f);
        // Buttons use the default font only for glyph buttons; catalog typography is
        // set per-book through CreateLabel above.
        GameObject label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(button.transform, false);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        Stretch(labelRect);
        TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = 42f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return button.GetComponent<Button>();
    }

    private static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = color;
        return panel;
    }

    private static void ClearChildren(Transform parent)
    {
        foreach (Transform child in parent) Destroy(child.gameObject);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private sealed class BookItemCard
    {
        public readonly ShopItemDefinition Item;
        public readonly RectTransform Rect;
        public BookItemCard(ShopItemDefinition item, RectTransform rect) { Item = item; Rect = rect; }
    }
}
