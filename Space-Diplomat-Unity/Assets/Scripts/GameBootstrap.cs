using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
public class GameBootstrap : MonoBehaviour
{
    public static bool BootDone { get; private set; } = false;
    public static event System.Action Booted;

    private static string SERVER_BASE => ServerConfig.BaseUrl;
    private static string API_RESET => SERVER_BASE + "/reset_affect";

    // Awake is called when the script instance is being loaded
    private void Awake()
    {
        // If a second Bootstrap sneaks in, skip
        if (BootDone)
        {
            Destroy(gameObject);
            return;
        }

        // Keep this around so only one exists across scenes
        DontDestroyOnLoad(gameObject);

        // Kick-off the one-time inicialization
        StartCoroutine(Boot());
    }

    private IEnumerator Boot()
    {
        EnsureNeutralIfNewGame("ZAXIN");
        EnsureNeutralIfNewGame("PENBOL");
        EnsureNeutralIfNewGame("BRAXIM");

        // Server-side reset
        yield return ResetServerAffect();

        // Notify UI to repaint
        GameState.Instance.RaiseEmotionsChanged(null);

        // Unblock everyone else
        BootDone = true;
        Booted?.Invoke();
    }

    private void EnsureNeutralIfNewGame(string alien)
    {
        var data = GameState.Instance.GetAlienData(alien);
        if (data.chatHistory.Count == 0)
        {
            data.emotionCounts["joy"] = 0.2f;
            data.emotionCounts["sadness"] = 0.2f;
            data.emotionCounts["anger"] = 0.2f;
            data.emotionCounts["fear"] = 0.2f;
            data.emotionCounts["disgust"] = 0.2f;
            data.lastEmotionKey = "Neutral";
        }
    }

    IEnumerator ResetServerAffect()
    {
        var json = "{}";
        var req = new UnityWebRequest(API_RESET, "POST");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning("Reset affect failed:" + req.error);
    }
}
