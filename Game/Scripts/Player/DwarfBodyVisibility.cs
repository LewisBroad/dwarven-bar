using UnityEngine;

public class DwarfBodyVisibility : MonoBehaviour
{
    [Header("Target Mesh/Armature")]
    [Tooltip("The visual root containing your SkinnedMeshRenderer(s) and bones.")]
    [SerializeField] private GameObject visualModelRoot;

    [Header("Layer Names")]
    [SerializeField] private string hiddenLocalLayer = "LocalPlayerBody";
    [SerializeField] private string visibleLayer = "Default";

    [Header("Camera & Audio")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;

    public bool IsLocalPlayer { get; private set; } = true;

    private void Start()
    {
        // For singleplayer/testing, defaults to true.
        // In multiplayer, initialize this via your spawn/network hook.
        SetupOwnership(true);
    }

    /// <summary>
    /// Call this when spawning to configure whether this client owns the character.
    /// Never call this just to toggle mesh visibility.
    /// </summary>
    public void SetupOwnership(bool isLocal)
    {
        IsLocalPlayer = isLocal;

        if (playerCamera != null) playerCamera.gameObject.SetActive(isLocal);
        if (audioListener != null) audioListener.enabled = isLocal;

        // If local, hide body from first-person view. If remote, show body.
        SetMeshVisible(!isLocal);
    }

    /// <summary>
    /// Safely changes only the rendering layer of the mesh without touching cameras.
    /// </summary>
    public void SetMeshVisible(bool visible)
    {
        if (visualModelRoot == null) return;

        string targetLayerName = visible ? visibleLayer : hiddenLocalLayer;
        int layer = LayerMask.NameToLayer(targetLayerName);

        SetLayerRecursively(visualModelRoot, layer);
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}