using UnityEngine;

/// <summary>Interactable door sign that opens the bar from setup and closes it during service.</summary>
public class OpenCloseSign : MonoBehaviour, IInteractable
{
    [SerializeField] private QuotaGameManager gameManager;
    [SerializeField] private Transform signVisual;
    [SerializeField] private Vector3 openRotation = new(0f, 0f, 0f);
    [SerializeField] private Vector3 closedRotation = new(0f, 180f, 0f);

    private FloatingPromptAnchor _prompt;
    private InteractableHighlight _highlight;

    private void Awake()
    {
        if (gameManager == null) gameManager = QuotaGameManager.Instance;
        _prompt = GetComponent<FloatingPromptAnchor>();
        if (_prompt == null) _prompt = gameObject.AddComponent<FloatingPromptAnchor>();
        _highlight = GetComponent<InteractableHighlight>();
        if (_highlight == null) _highlight = gameObject.AddComponent<InteractableHighlight>();
    }

    private void Update()
    {
        if (gameManager == null) gameManager = QuotaGameManager.Instance;
        if (signVisual != null && gameManager != null)
        {
            signVisual.localRotation = Quaternion.Euler(gameManager.IsBarOpen ? openRotation : closedRotation);
        }
    }

    public string GetInteractionPrompt() => gameManager != null ? gameManager.GetSignPrompt() : "{KEY} Open Bar";
    public void Interact(DwarfInteractor interactor)
    {
        if (gameManager == null) return;
        gameManager.RequestToggleBar();
    }
    public void OnHoverEnter(string key) { _highlight.SetHighlight(true); _prompt.Show(GetInteractionPrompt().Replace("{KEY}", $"[{key}]")); }
    public void OnHoverExit() { _highlight.SetHighlight(false); _prompt.Hide(); }
}
