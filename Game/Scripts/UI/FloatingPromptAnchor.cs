using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Keeps a world-space prompt attached to an interactable and facing the player camera.</summary>
public class FloatingPromptAnchor : MonoBehaviour
{
    [Header("Anchor")]
    [SerializeField] private Transform customAnchorPoint;
    [SerializeField] private float heightOffset = 0.25f;
    [SerializeField] private float sideOffset = 0.15f;

    [Header("Animation")]
    [SerializeField] private float popSpeed = 12f;
    [SerializeField] private float targetScale = 0.0035f;

    [Header("World-space UI")]
    [SerializeField] private Canvas worldCanvas;
    [SerializeField] private TextMeshProUGUI promptLabel;

    private Camera _viewerCamera;
    private bool _isVisible;

    private void Awake()
    {
        EnsurePromptUI();

        // Follow the interactable through code rather than inheriting its transform.
        // This prevents a non-uniformly scaled prop (such as the pump) from stretching
        // the world-space Canvas and its text.
        if (worldCanvas != null) worldCanvas.transform.SetParent(null, true);
        SetCanvasScale(0f);
        if (worldCanvas != null) worldCanvas.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (worldCanvas == null) return;
        if (_viewerCamera == null)
        {
            _viewerCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        }

        if (_viewerCamera != null)
        {
            Transform anchor = customAnchorPoint != null ? customAnchorPoint : transform;
            Transform cameraTransform = _viewerCamera.transform;
            Vector3 promptPosition = anchor.position + Vector3.up * heightOffset + cameraTransform.right * sideOffset;
            worldCanvas.transform.position = promptPosition;

            Vector3 cameraDirection = cameraTransform.position - promptPosition;
            if (cameraDirection.sqrMagnitude > 0.001f)
            {
                // Only rotate the canvas: rotating this object would rotate the interactable too.
                worldCanvas.transform.rotation = Quaternion.LookRotation(cameraDirection, cameraTransform.up)
                    * Quaternion.Euler(0f, 180f, 0f);
            }
        }

        float nextScale = Mathf.Lerp(worldCanvas.transform.localScale.x, _isVisible ? targetScale : 0f, Time.deltaTime * popSpeed);
        SetCanvasScale(nextScale);

        if (!_isVisible && nextScale < 0.00001f)
        {
            SetCanvasScale(0f);
            worldCanvas.gameObject.SetActive(false);
        }
    }

    public void Show(string text)
    {
        EnsurePromptUI();
        if (worldCanvas == null) return;
        if (promptLabel != null) promptLabel.text = text;

        _isVisible = !string.IsNullOrWhiteSpace(text);
        worldCanvas.gameObject.SetActive(_isVisible);
    }

    public void Hide() => _isVisible = false;

    public void SetViewerCamera(Camera viewerCamera)
    {
        if (viewerCamera == null) return;

        _viewerCamera = viewerCamera;
        if (worldCanvas != null) worldCanvas.worldCamera = viewerCamera;
    }

    private void SetCanvasScale(float scale)
    {
        if (worldCanvas != null) worldCanvas.transform.localScale = Vector3.one * scale;
    }

    private void EnsurePromptUI()
    {
        if (worldCanvas == null)
        {
            GameObject canvasObject = new GameObject("Interaction Prompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            worldCanvas = canvasObject.GetComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        }

        if (promptLabel == null)
        {
            GameObject labelObject = new GameObject("Prompt Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(worldCanvas.transform, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(420f, 80f);

            promptLabel = labelObject.GetComponent<TextMeshProUGUI>();
            promptLabel.font = TMP_Settings.defaultFontAsset;
            promptLabel.alignment = TextAlignmentOptions.Center;
            promptLabel.fontSize = 32f;
            promptLabel.enableWordWrapping = false;
            promptLabel.raycastTarget = false;
        }
    }
}
