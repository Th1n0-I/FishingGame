using UnityEngine;
using UnityEngine.UI;

public class NoiseController : MonoBehaviour {
	
	[SerializeField] private int seed = 0;
	
	[SerializeField] private ComputeShader shader;
	[SerializeField] private RenderTexture renderTexture;

	[SerializeField] private bool onRawImage;
	[SerializeField] private int  dotAmount;
	[SerializeField, Range(0,1)] private float z;
	
	private RawImage rawImage;
	

	private void Start() {
		
		
		
		renderTexture                   = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGBFloat);
		renderTexture.dimension         = UnityEngine.Rendering.TextureDimension.Tex3D;
		renderTexture.volumeDepth       = 128;
		renderTexture.enableRandomWrite = true;
		renderTexture.wrapMode = TextureWrapMode.Repeat;
		renderTexture.Create();
		
		Vector3[] dots = new Vector3[dotAmount * dotAmount * dotAmount];
		
		Random.InitState(seed);
		
		for (int i = 0; i < dotAmount; i++) {
			for (int j = 0; j < dotAmount; j++) {
				for (int k = 0; k < dotAmount; k++) {
					float r1, r2, r3;
					r1                  = Random.value;
					r2                  = Random.value;
					r3                  = Random.value;
					dots[i*dotAmount*dotAmount+j*dotAmount+k] = new Vector3(r1, r2, r3);
				}
			}
		}
		
		Vector4[] longDots = new Vector4[dotAmount * dotAmount * dotAmount];

		for (int i = 0; i < dotAmount * dotAmount * dotAmount; i++) {
			longDots[i] = new Vector4(dots[i].x, dots[i].y ,dots[i].z,1);
		}
		
		shader.SetTexture(0, "Result", renderTexture);
		shader.SetFloat("Resolution", renderTexture.width);
		shader.SetVectorArray("Points", longDots);
		shader.SetInt("PointCount", dotAmount *  dotAmount);
		shader.Dispatch(0, renderTexture.width / 8, renderTexture.height / 8, renderTexture.volumeDepth / 8);
		Shader.SetGlobalTexture("_NoiseTex", renderTexture);
		
		if (onRawImage) {
			rawImage = GetComponent<RawImage>();
			//rawImage.texture = renderTexture;
		}
	}

	private void Update() {
		
		if (onRawImage) {
			//rawImage.texture = renderTexture;
		}
	} 

}
