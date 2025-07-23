using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    public Transform player;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Restore player's position inside spaceship if there is any
        if (PlayerData.hasSavedPositionShip && !PlayerData.IsInGalaxyMap)
        {
            player.position = PlayerData.savedPositionShip; // Set the player's position to the saved position
            player.rotation = PlayerData.savedRotationShip; // Set the player's rotation to the saved rotation
            PlayerData.hasSavedPositionShip = false; // Reset the flag after restoring position
        }

        // Restore player's position in space map if there is any
        if (PlayerData.hasSavedPositionSpace && PlayerData.IsInGalaxyMap)
        {
            player.position = PlayerData.savedPositionSpace; // Set the player's position to the saved position
            player.rotation = PlayerData.savedRotationSpace; // Set the player's rotation to the saved rotation
            PlayerData.hasSavedPositionSpace = false; // Reset the flag after restoring position
        }
    }
}
