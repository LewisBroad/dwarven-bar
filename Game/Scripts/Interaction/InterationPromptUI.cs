using TMPro;
using UnityEngine;

public class InteractionPromptUI : MonoBehaviour
{
    public static InteractionPromptUI Instance { get; private set; }

    [SerializeField] private GameObject promptPanel;
    [SerializeField] private TextMeshProUGUI promptText;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        HidePrompt();
    }

    public void ShowPrompt(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            HidePrompt();
            return;
        }

        if (promptPanel != null) promptPanel.SetActive(true);
        if (promptText != null) promptText.text = text;
    }

    public void HidePrompt()
    {
        if (promptPanel != null) promptPanel.SetActive(false);
        if (promptText != null) promptText.text = string.Empty;
    }
}