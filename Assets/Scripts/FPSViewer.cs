using TMPro;
using UnityEngine;

public class FPSViewer : MonoBehaviour {
	private string          prefix;
	private TextMeshProUGUI text;
	private float           staticFrameTime;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        prefix = "FPS: ";
        text = GetComponent<TextMeshProUGUI>();
    }

    // Update is called once per frame
    void Update()
    {
	    var frameTime =  Time.deltaTime;
	    staticFrameTime = Mathf.Lerp(staticFrameTime, frameTime, 0.02f);
	    float fps = 1.0f                                    / staticFrameTime;
        text.text       = prefix + (Mathf.Round(fps * 100f) / 100f).ToString();
    }
}
