using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // The target to follow (e.g., the spaceship)
    public Vector3 positionOffset = new Vector3(0f, 1200f, 0f); // Position offset from the target position
    public Vector3 rotationOffset = new Vector3(90f, 0f, 0f); // Rotation offset from the target position
    public float smoothSpeed = 5f; // Speed of camera smoothing

    // LateUpdate is called once per frame after all Update methods have been called
    void LateUpdate()
    {
        if (target == null) return; // If no target is set, do nothing

        // Calculate the desired position based on the target's position and the offset
        Vector3 desiredPosition = target.position + positionOffset;

        // Smoothly interpolate between the current position and the desired position
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * smoothSpeed);

        // Update the camera's position
        transform.position = smoothedPosition;

        // Always look straight down
        transform.rotation = Quaternion.Euler(rotationOffset);
    }
}
