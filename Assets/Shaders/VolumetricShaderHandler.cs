using UnityEngine;

public class VolumetricShaderHandler : MonoBehaviour {
	[SerializeField] private ComputeShader shader;
	[SerializeField] private int textureDivide = 2;
	
	private RenderTexture renderTexture;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        renderTexture = new RenderTexture(Screen.width / textureDivide, Screen.height / textureDivide, 24);
        renderTexture.enableRandomWrite = true;
        renderTexture.Create();
    }

    // Update is called once per frame
    void Update()
    {
	    shader.SetTexture(0, "Result", renderTexture);
	    shader.Dispatch(0, renderTexture.width / 8, renderTexture.height / 8, renderTexture.volumeDepth / 8);
	    Shader.SetGlobalTexture("_VolumetricFog", renderTexture);
    }
}
