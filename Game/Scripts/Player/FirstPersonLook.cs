using UnityEngine;

public class FirstPersonLook : MonoBehaviour
{
    [Header("Sensitivity")]
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float stickSensitivity = 120f; // Degrees per second for controller sticks

    [Header("Pitch Limits")]
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    [Header("References")]
    [SerializeField] private Transform playerBody;

    private float _pitch;

    public void HandleLook(Vector2 lookInput, bool isGamepad)
    {
        float mouseX;
        float mouseY;

        if (isGamepad)
        {
            // Controller sticks provide normalized values (-1 to 1), scale by deltaTime
            mouseX = lookInput.x * stickSensitivity * Time.deltaTime;
            mouseY = lookInput.y * stickSensitivity * Time.deltaTime;
        }
        else
        {
            // Mouse delta is an instantaneous frame delta
            mouseX = lookInput.x * mouseSensitivity;
            mouseY = lookInput.y * mouseSensitivity;
        }

        _pitch -= mouseY;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        // Vertical pitch on camera
        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        
        // Horizontal yaw on dwarf body
        playerBody.Rotate(Vector3.up * mouseX);
    }
}