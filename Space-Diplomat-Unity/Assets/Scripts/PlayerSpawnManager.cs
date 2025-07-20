using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    public Transform player;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Restore player's position if there is any
        if (PlayerData.hasSavedPosition)
        {
            player.position = PlayerData.savedPosition; // Set the player's position to the saved position
            player.rotation = PlayerData.savedRotation; // Set the player's rotation to the saved rotation
            PlayerData.hasSavedPosition = false; // Reset the flag after restoring position
        }
    }
}
