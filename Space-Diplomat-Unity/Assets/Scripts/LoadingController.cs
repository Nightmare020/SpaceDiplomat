using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LoadingController : MonoBehaviour
{
    [Header("Next scene to load")]
    public string nextSceneName = "SpaceshipMovementScene";

    [Header("Optional progress UI")]
    public Slider progressBar;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private IEnumerator Start()
    {
        // Ensure your GameState exists early
        if (GameState.Instance == null)
            new GameObject("GameState").AddComponent<GameState>();

        // Kick off async load
        yield return null; // one frame
        AsyncOperation op = SceneManager.LoadSceneAsync(nextSceneName, LoadSceneMode.Single);
        op.allowSceneActivation = false;

        // Smoothly update progress until 0.9
        while (op.progress < 0.9f)
        {
            UpdateProgress(op.progress); // 0..0.9
            yield return null;
        }

        // Fill to 100% for UX
        float p = 0.9f;
        while (p < 1f)
        {
            p = Mathf.MoveTowards(p, 1f, Time.deltaTime);
            UpdateProgress(p);
            yield return null;
        }

        op.allowSceneActivation = true;
    }

    void UpdateProgress(float value01)
    {
        if (progressBar) 
            progressBar.value = value01;
    }
}
