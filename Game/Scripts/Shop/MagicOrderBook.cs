using UnityEngine;
using TMPro;

/// <summary>World interactable that opens the magic ordering catalog.</summary>
public class MagicOrderBook : MonoBehaviour, IInteractable
{
    [SerializeField] private ShopOrderManager orderManager;
    [SerializeField] private MagicShopCatalog catalogOverride;
    [Tooltip("Position and rotation used while reading. Aim this transform at the open book pages.")]
    [SerializeField] private Transform bookViewAnchor;
    [Tooltip("World-space UI surface placed just above the open book pages.")]
    [SerializeField] private Transform bookUiAnchor;
    [Tooltip("World-space scale of the open book UI. 0.001 makes the 1440px spread 1.44m wide.")]
    [SerializeField, Min(0.0001f)] private float bookUiScale = 0.001f;
    [Tooltip("Seconds used to ease the camera into and out of the reading view.")]
    [SerializeField, Min(0f)] private float cameraTransitionDuration = 0.25f;
    [Header("Book Typography")]
    [SerializeField] private TMP_FontAsset bookFont;

    private InteractableHighlight _highlight;
    private FloatingPromptAnchor _prompt;

    private void Awake()
    {
        _highlight = GetComponent<InteractableHighlight>();
        if (_highlight == null) _highlight = gameObject.AddComponent<InteractableHighlight>();
        _prompt = GetComponent<FloatingPromptAnchor>();
        if (_prompt == null) _prompt = gameObject.AddComponent<FloatingPromptAnchor>();
        if (orderManager == null) orderManager = ShopOrderManager.Instance;
    }

    public string GetInteractionPrompt() => "{KEY} Open Magic Orders";

    public void Interact(DwarfInteractor interactor)
    {
        if (orderManager == null) orderManager = ShopOrderManager.Instance;
        Camera playerCamera = interactor != null ? interactor.GetComponent<Camera>() : null;
        MagicOrderBookUI.Open(
            orderManager,
            catalogOverride != null ? catalogOverride : orderManager != null ? orderManager.Catalog : null,
            playerCamera,
            bookViewAnchor,
            bookUiAnchor,
            bookUiScale,
            bookFont,
            cameraTransitionDuration);
    }

    public void OnHoverEnter(string boundKeyName)
    {
        if (MagicOrderBookUI.IsOpen)
        {
            _highlight.SetHighlight(false);
            _prompt.Hide();
            return;
        }

        _highlight.SetHighlight(true);
        _prompt.Show(GetInteractionPrompt().Replace("{KEY}", $"[{boundKeyName}]"));
    }

    public void OnHoverExit()
    {
        _highlight.SetHighlight(false);
        _prompt.Hide();
    }
}
