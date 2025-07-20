using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // The target the camera will follow
    public Vector3 offset = new Vector3(0, 5f, -10f);
    public float smoothSpeed = 5f; // Speed of the camera's movement

    // Start is called before the first frame update
    void Start()
    {
        if (target != null)
        {
            // Rotate the offset to match the ships rotation
            Vector3 rotatedOffset = target.rotation * offset;

            // Immediately position the camera behind the ship
            transform.position = target.position + rotatedOffset;

            // Make the camera look at the ship
            transform.LookAt(target);
        }
    }

    // LateUpdate is called after all Update methods have been called
    void LateUpdate()
    {
        if (target == null) return;

        // Rotate the offset with the ship's orientation
        Vector3 rotatedOffset = target.rotation * offset;

        // Desired position is behind the ship
        Vector3 desiredPosition = target.position + rotatedOffset;

        // Smooth camera movement
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        // Look at the ship
        transform.LookAt(target);
    }
}
