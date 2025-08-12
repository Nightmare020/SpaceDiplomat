using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LoadingDots : MonoBehaviour
{
    public TextMeshProUGUI text;
    public string baseText = "Loading";
    public float interval = 0.35f;

    float t; int dots;

    // Update is called once per frame
    void Update()
    {
        t += Time.deltaTime;
        if (t >= interval)
        {
            t = 0f; dots = (dots + 1) % 4;
            text.text = baseText + new string('.', dots);
        }
    }

    private void Reset() 
    { 
        text = GetComponent<TextMeshProUGUI>();
    }

}
