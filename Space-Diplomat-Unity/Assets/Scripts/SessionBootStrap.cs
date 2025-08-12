using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class SessionBootStrap : MonoBehaviour
{
    // Awake is called when the script instance is being loaded
    private void Awake()
    {
        // Make sure GameState exists
        if (GameState.Instance == null)
            new GameObject("GameState").AddComponent<GameState>();

        // Start a brand new run (new sessionId + clear locally memory + ensure next call reset server)
        GameState.Instance.StartNewGameSession();

        // Reset the server right now so Penbol is neutral before any charts/chat
        StartCoroutine(ResetServerAffectNow());
    }

    IEnumerator ResetServerAffectNow()
    {
        var url = ServerConfig.BaseUrl + "/reset_affect";
        var req = new UnityWebRequest(url, "POST");
        var body = System.Text.Encoding.UTF8.GetBytes("{}");
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 10;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            GameState.Instance.MarkServerAffectReset();
        else
            Debug.LogWarning("reset_affect at startup failed: " + req.error);
    }
}
