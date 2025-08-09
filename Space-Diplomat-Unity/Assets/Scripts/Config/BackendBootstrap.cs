using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class BackendBootstrap : MonoBehaviour
{
    [SerializeField] float timeoutSeconds = 5f;
    [SerializeField] bool persistAcrossScenes = true;

    // Awake is called when the script instance is being loaded
    private void Awake()
    {
        if (persistAcrossScenes)
            DontDestroyOnLoad(gameObject);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        StartCoroutine(EnsureBackend());
    }

    IEnumerator EnsureBackend()
    {
        string healthUrl = ServerConfig.BaseUrl + "/health";
        using (var req = UnityWebRequest.Get(healthUrl))
        {
            req.timeout = Mathf.CeilToInt(timeoutSeconds);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // Remote not reachable -> fall back to local dev server
                ServerConfig.OverrideBaseUrl("http://127.0.0.1:5000");
                Debug.LogWarning($"Backend not reachable at {healthUrl}. Falling back to {ServerConfig.BaseUrl}");
            }
            else
            {
                Debug.Log($"Backend OK at {healthUrl}");
            }
        }
    }
}
