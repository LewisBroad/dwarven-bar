using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Draws a white silhouette outline around an interactable while it is hovered.</summary>
public class InteractableHighlight : MonoBehaviour
{
    [Header("Outline")]
    [ColorUsage(true, true)]
    [SerializeField] private Color outlineColor = Color.white;
    [SerializeField, Min(0.001f)] private float outlineWidth = 0.025f;

    private readonly List<Renderer> _outlineRenderers = new();
    private Material _outlineMaterial;

    private void Awake()
    {
        Shader outlineShader = Shader.Find("Custom/InteractableWhiteOutline");
        if (outlineShader == null)
        {
            Debug.LogError("Interactable outline shader was not found.", this);
            enabled = false;
            return;
        }

        _outlineMaterial = new Material(outlineShader) { name = $"{name} Outline Material" };
        _outlineMaterial.SetColor("_OutlineColor", outlineColor);
        _outlineMaterial.SetFloat("_OutlineWidth", outlineWidth);

        foreach (MeshFilter sourceFilter in GetComponentsInChildren<MeshFilter>())
        {
            MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
            if (sourceRenderer == null || sourceFilter.sharedMesh == null) continue;

            GameObject outlineObject = new GameObject($"{sourceFilter.name} Outline");
            outlineObject.transform.SetParent(sourceFilter.transform, false);

            outlineObject.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer outlineRenderer = outlineObject.AddComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = _outlineMaterial;
            outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            outlineRenderer.enabled = false;
            _outlineRenderers.Add(outlineRenderer);
        }
    }

    private void OnDestroy()
    {
        if (_outlineMaterial != null) Destroy(_outlineMaterial);
    }

    public void SetHighlight(bool active)
    {
        foreach (Renderer outlineRenderer in _outlineRenderers)
        {
            if (outlineRenderer != null) outlineRenderer.enabled = active;
        }
    }
}
