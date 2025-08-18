using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Linq;
using System.Collections;

public class DonutBuilder : MonoBehaviour
{
    private static string SERVER_BASE => ServerConfig.BaseUrl;
    private static string API_CHAT => SERVER_BASE + "/chat";
    private static string API_STATE => SERVER_BASE + "/alien_state";
    private static string API_HEALTH => SERVER_BASE + "/health";


    [System.Serializable]
    public class  Slice
    {
        public string emotionKey; // The key for the emotion
        public Image img; // The sprite for the emotion
    }

    [System.Serializable]
    private class AlienStateResponse
    {
        public string alien;
        public string distributionJson; // Emotion distribution as a JSON string
        public State state;
        public float joyThreshold; // Joy threshold for negotiation success
        public float angerTolerance; // Anger tolerance for negotiation failure 

        [System.Serializable]
        public class State
        {
            public float joy; // Joy emotion value
            public float anger; // Anger emotion value
        }
    }

    [System.Serializable]
    private class AlienNamePayload { public string alienName; }

    [System.Serializable]
    public class DistributionWrapper
    {
        public string[] keys; // Array of emotion keys
        public float[] values; // Corresponding array of emotion values
    }

    public List<Slice> slices = new List<Slice>(); // List of slices representing emotions
    public Image centerMask; // The center mask image

    // Control layout explicitly
    [Header("Layout")]
    [SerializeField] RectTransform chartRoot; // parent rect that contains all slices + center
    [SerializeField] float diameter = 600f; // pixels (befire CanvasScaler)
    [Range(0.1f, 0.9f)]
    [SerializeField] float innerHoleRatio = 0.58f; // inner hole = diameter * this
    [SerializeField] private string targetAlienOverride;

    // Fixed order around the circle (clockwise)
    static readonly string[] Order = { "disgust", "fear", "anger", "sadness", "joy"};

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Refresh();
    }

    private void OnEnable()
    {
        // Normalize rects in case layout changed
        NormalizeRects();

        // Try to get a fresh snapshot from server, the refresh UI
        StartCoroutine(InitSocialChart());

        GameState.EmotionsChanged += OnEmotionsChanged;
    }

    private void OnDisable()
    {
        GameState.EmotionsChanged -= OnEmotionsChanged;
    }

    private string TargetAlienName
    {
        get 
        { 
            var n = (targetAlienOverride ?? "").Trim();
            return string.IsNullOrEmpty(n) ? PlayerData.SelectedAlienName : n;
        }
    }

    // Force identical anchors/size/position for all chart pieces
    void NormalizeRects()
    {
        if (chartRoot == null)
        {
            chartRoot = GetComponent<RectTransform>(); // fallback
        }

        Vector2 size = new Vector2(diameter, diameter);

        foreach (var sl in slices)
        {
            if (sl?.img == null) continue;
            RectTransform rt = sl.img.rectTransform;
            SnapToCenter(rt, size);
            sl.img.preserveAspect = true;
        }

        if (centerMask != null)
        {
            RectTransform rt = centerMask.rectTransform;
            SnapToCenter(rt, size * innerHoleRatio);
            centerMask.preserveAspect = true;
        }
    }

    private IEnumerator FetchAndApplyState()
    {
        string alien = TargetAlienName;
        if (string.IsNullOrEmpty(alien)) yield break;

        var payload = JsonUtility.ToJson(new AlienNamePayload { alienName = alien });

        using (var req = new UnityWebRequest(API_STATE, "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(payload);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<AlienStateResponse>(req.downloadHandler.text);

                // Update GameState so any other UI uses the same, fresh numbers
                var data = GameState.Instance.GetAlienData(alien);
                var dist = JsonUtility.FromJson<DistributionWrapper>(response.distributionJson);

                data.emotionCounts.Clear();
                for (int i = 0; i < dist.keys.Length; i++)
                {
                    data.emotionCounts[dist.keys[i]] = dist.values[i];
                }

                GameState.Instance.RaiseEmotionsChanged(alien);

                // Now repaint this donut
                Refresh();
            }
            else
            {
                // Fallback: just paint whatever GameState has
                Refresh();
                Debug.LogWarning("DonutBuilder: alien_state fetch failed: " + req.error);
            }
        }
    }

    public void Refresh()
    {
        string alien = TargetAlienName; // Get the selected alien name from PlayerData
        if (string.IsNullOrEmpty(alien))
        {
            // No alien selected -> Hide the center mask completely
            gameObject.SetActive(false);
            return;
        }

        var counts = GameState.Instance.GetAlienData(alien).emotionCounts; // Get the emotion counts for the selected alien

        // Lowercase keys for safety
        Dictionary<string, float> keyValuePairs = new Dictionary<string, float>();
        foreach (var key in Order)
        {
            counts.TryGetValue(key, out float keyValue);
            keyValuePairs[key] = Mathf.Max(0f, keyValue);
        }

        // Percentages
        float total = keyValuePairs.Values.Sum();
        if (total <= 0.0001f)
        {
            foreach (var key in Order)
            {
                keyValuePairs[key] = 1f; // All 1 -> normalized to 0.2 each
            }

            total = keyValuePairs.Values.Sum();
        }

        // Normalize to [0..1] so they sum to 1
        Dictionary<string, float> weights = new Dictionary<string, float>();
        foreach (var key in Order)
        {
            weights[key] = keyValuePairs[key] / total;
        }

        // Find the UI images by key (ensure all 5 exist)
        Dictionary<string, Slice> byKey = new Dictionary<string, Slice>();
        foreach (var slice in slices)
        {
            if (slice == null || slice.img == null) continue;
            byKey[slice.emotionKey?.ToLower() ?? ""] = slice;
        }

        // Lay out wedges with direction rules
        float cursor = 0f; // accumulated angle in turns (0..1), clockwise baseline

        foreach (var key in Order)
        {
            if (!byKey.TryGetValue(key, out var sl) || sl.img == null)
                continue;

            float wedge = Mathf.Clamp01(weights[key]);
            var img = sl.img;

            // Make every slice radial - 360
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;

            if (key == "joy")
            {
                // Joy grows anti-clockwise
                img.fillClockwise = false;
                img.fillAmount = wedge;

                // Rotate so that the end of joy aligns with the running cursor
                // (keep seams tight)
                float startAngle = -(cursor + wedge) * 360f;
                img.transform.localEulerAngles = new Vector3(0f, 0f, startAngle);
            }
            else
            {
                // Others grow clockwise
                img.fillClockwise = true;
                img.fillAmount = wedge;

                // Start at current cursor angle
                float startAngle = -cursor * 360f;
                img.transform.localEulerAngles = new Vector3(0f, 0f, startAngle);
            }

            // Advance along the ring for the next slice
            cursor += wedge;
        }

        // Keep the mask on top so it hides the middle of the donut
        if (centerMask != null)
        {
            centerMask.transform.SetAsLastSibling(); // Set the center mask as the last sibling to keep it on top
        }
    }

    static void SnapToCenter(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    private void OnEmotionsChanged(string alienName)
    {
        string current = TargetAlienName;
        if (string.IsNullOrEmpty(current))
        {
            return;
        }
        
        StartCoroutine(FetchAndApplyState());
        Refresh();
    }

    IEnumerator InitSocialChart()
    {
        if (!GameState.Instance.serverAffectResetDone)
        {
            yield return StartCoroutine(ResetServerAffectOnce());
            GameState.Instance.MarkServerAffectReset();
        }
        yield return StartCoroutine(FetchAndApplyState());
    }

    IEnumerator ResetServerAffectOnce()
    {
        var url = ServerConfig.BaseUrl + "/reset_affect";
        var req = new UnityWebRequest(url, "POST");
        var body = System.Text.Encoding.UTF8.GetBytes("{}");
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 10;
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning("reset_affect failed: " + req.error);
    }
}
