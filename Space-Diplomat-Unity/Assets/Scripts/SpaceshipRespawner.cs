using UnityEngine;
using System.Collections;

public class SpaceshipRespawner : MonoBehaviour
{
    public SpaceshipController spaceship;

    public void Respawn (float delay)
    {
        // Start the respawn coroutine with the specified delay
        StartCoroutine(RespawnAfterDelay(delay));
    }

    private IEnumerator RespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay); // Wait for the specified delay

        // Respawn the spaceship at the respawn point
        spaceship.transform.position = spaceship.respawnPoint.position; // Set the position to the respawn point
        spaceship.transform.rotation = spaceship.respawnPoint.rotation; // Set the rotation to the respawn point

        spaceship.rb.linearVelocity = Vector3.zero; // Reset the linear velocity
        spaceship.rb.angularVelocity = Vector3.zero; // Reset the angular velocity

        spaceship.sunWarning.SetActive(false); // Hide the sun warning UI element

        spaceship.gameObject.SetActive(true); // Reactivate the spaceship game object
    }
}
