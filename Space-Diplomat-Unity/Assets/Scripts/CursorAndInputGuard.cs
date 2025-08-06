using UnityEngine;

public class CursorAndInputGuard : MonoBehaviour
{
    private void OnEnable()
    {
        // Disbale astronaut controller if it exists
        var playerController = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (playerController != null)
        {
            playerController.enabled = false;
        }

        // Force a free cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDisable()
    {
        // When leaving the scene, re-enable the astronaut controller
        var playerController = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (playerController != null)
        {
            playerController.enabled = true;
        }
    }

    // Windows/macOS re-lock cursor on focus change, so let's avoid that
    void OnApplicationFocus(bool focus)
    {
        if (focus)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // If something else hides it mid-frame, pop it back
    private void LateUpdate()
    {
        if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
