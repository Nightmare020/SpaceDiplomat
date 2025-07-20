using UnityEngine;

public static class PlayerData
{
    public static Vector3 savedPosition = Vector3.zero; // Position to save the player's position
    public static Quaternion savedRotation = Quaternion.identity; // Rotation to save the player's rotation
    public static bool hasSavedPosition = false; // Flag to check if the player has a saved position
}
