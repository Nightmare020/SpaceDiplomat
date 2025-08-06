using UnityEngine;
using UnityEngine.SceneManagement;

public class BackToShip : MonoBehaviour
{
    [SerializeField] string sceneName = "SpaceshipMovementScene";

    private void OnEnable()
    {
        if (InputService.Instance != null)
        {
            // Use the same event as go back to spaceship
            InputService.Instance.shipReturnToShip += GoBack;
        }
    }

    private void OnDisable()
    {
        if (InputService.Instance != null)
        {
            InputService.Instance.shipReturnToShip -= GoBack;
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Fallback for keyboard in case InputService context is wrong
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            GoBack();
        }
    }

    private void GoBack()
    {
        // Restore gameplay context if needed
        if (InputService.Instance != null)
        {
            InputService.Instance.SwitchContext(GameInputContext.Astronaut);
        }

        SceneManager.LoadScene(sceneName);
    }
}
