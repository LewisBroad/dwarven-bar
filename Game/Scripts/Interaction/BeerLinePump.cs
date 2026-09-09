using UnityEngine;

public class BeerLinePump : MonoBehaviour, IInteractable
{
    [Header("Line Link")]
    [SerializeField] private BasementKegBay linkedBay;

    [Header("Lever Visuals & Sound")]
    [SerializeField] private Transform leverHandle;
    [SerializeField] private Vector3 offRotation = new Vector3(35f, 0, 0);
    [SerializeField] private Vector3 onRotation = new Vector3(-35f, 0, 0);
    [SerializeField] private AudioSource leverAudio;
    private InteractableHighlight _highlighter;
    [SerializeField] private FloatingPromptAnchor promptAnchor;

    public bool IsPumping { get; private set; } = false;


    private void Awake(){
        _highlighter = GetComponent<InteractableHighlight>();
        if (_highlighter == null) _highlighter = gameObject.AddComponent<InteractableHighlight>();
        if (promptAnchor == null) promptAnchor = GetComponentInChildren<FloatingPromptAnchor>();
        if (promptAnchor == null) promptAnchor = gameObject.AddComponent<FloatingPromptAnchor>();
    }
    private void Start()
    {
        // Force the off state explicitly on start
        SetPumpingState(false);
    }

    public string GetInteractionPrompt()
    {
        return IsPumping ? "Press [E] to STOP Line Pump" : "Press [E] to START Line Pump";
    }
    public void OnHoverEnter(string boundKeyName)
    {
        if (_highlighter != null) _highlighter.SetHighlight(true);

        if (promptAnchor != null)
        {
            string formattedPrompt = GetInteractionPrompt().Replace("{KEY}", $"[{boundKeyName}]");
            promptAnchor.Show(formattedPrompt);
        }
    }

    public void OnHoverExit()
    {
        if (_highlighter != null) _highlighter.SetHighlight(false);

        if (promptAnchor != null)
        {
            promptAnchor.Hide();
        }
    }

    public void Interact(DwarfInteractor interactor)
    {
        SetPumpingState(!IsPumping);

        if (leverAudio != null)
        {
            leverAudio.Play();
        }
    }

private void SetPumpingState(bool active)
{
    IsPumping = active;

    if (linkedBay != null)
    {
        linkedBay.SetPumpState(IsPumping);
    }

    if (leverHandle != null)
    {
        leverHandle.localRotation = Quaternion.Euler(IsPumping ? onRotation : offRotation);
    }
}
}
