public interface IInteractable
{
    string GetInteractionPrompt();
    void Interact(DwarfInteractor interactor);
    void OnHoverEnter(string boundKeyName);
    void OnHoverExit();
}