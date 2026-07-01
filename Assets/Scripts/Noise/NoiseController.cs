using System.Collections.Generic;
using GrayWolf.GPUInstancing.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class NoiseController : MonoBehaviour {

	[Header("Volumetrics")]
	[SerializeField] private int textureDivide = 2;
	[SerializeField] private float densityMultiplier = 1.0f;
	[SerializeField] private float densityThreshold  = 1.0f;
	[SerializeField] private float lightScattering   = 1.0f;
	[SerializeField] private float stepSize          = 1.0f;
	[SerializeField] private float noiseSize         = 1.0f;
	[SerializeField] private float yMin              = 60;
	[SerializeField] private float yMax              = 70;
	[SerializeField] private float maxDist           = 100;
	[SerializeField] private Color fogColor;
	[ColorUsage(true, true)]
	[SerializeField] private Color lightContribution;
	
	[Header("Other")]
	[SerializeField] private int seed = 0;
	[SerializeField] private ComputeShader noiseShader, volumetricsShader;
	[SerializeField] private RenderTexture noiseRenderTexture, volumetricsRenderTexture;
	[SerializeField] private bool onRawImage;
	[SerializeField] private int  dotAmount;
	[SerializeField, Range(0,1)] private float z;
	
	private RawImage rawImage;

	private void OnEnable() {
		RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
	}

	private void OnDisable() {
		RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
	}

	private void Start() {
		
		
		
		noiseRenderTexture                   = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGBFloat);
		noiseRenderTexture.dimension         = UnityEngine.Rendering.TextureDimension.Tex3D;
		noiseRenderTexture.volumeDepth       = 128;
		noiseRenderTexture.enableRandomWrite = true;
		noiseRenderTexture.wrapMode = TextureWrapMode.Repeat;
		noiseRenderTexture.Create();
		
		volumetricsRenderTexture = new RenderTexture(Screen.width / textureDivide, Screen.height / textureDivide, 24, RenderTextureFormat.ARGBFloat);
		volumetricsRenderTexture.enableRandomWrite = true;
		volumetricsRenderTexture.wrapMode = TextureWrapMode.Repeat;
		volumetricsRenderTexture.Create();
		
		
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
		
		noiseShader.SetTexture(0, "Result", noiseRenderTexture);
		noiseShader.SetFloat("Resolution", noiseRenderTexture.width);
		noiseShader.SetVectorArray("Points", longDots);
		noiseShader.SetInt("PointCount", dotAmount *  dotAmount);
		noiseShader.Dispatch(0, noiseRenderTexture.width / 8, noiseRenderTexture.height / 8, noiseRenderTexture.volumeDepth / 8);
		Shader.SetGlobalTexture("_NoiseTex", noiseRenderTexture);
		
		volumetricsShader.SetTexture(0, "NoiseTex", noiseRenderTexture);
		volumetricsShader.SetTexture(0, "Result", volumetricsRenderTexture);
		
		Shader.SetGlobalTexture("_VolumetricsTex", volumetricsRenderTexture);
		volumetricsShader.SetInt("TextureDivide", textureDivide);
		volumetricsShader.SetInt("ScreenWidth", Screen.width);
		volumetricsShader.SetInt("ScreenHeight", Screen.height);
		
		if (onRawImage) {
			rawImage = GetComponentInChildren<RawImage>();
			//rawImage.texture = renderTexture;
		}
	}

	private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam) {

		if (cam != Camera.main) return;

		var depthTex = PersistentDepthFeature.PersistentDepthTexture;

		if (depthTex != null && depthTex.rt != null) {
			var sun = RenderSettings.sun;
			
			volumetricsShader.SetTexture(0, "NoiseTex", noiseRenderTexture);
			volumetricsShader.SetTexture(0, "DepthTex", depthTex.rt);
			volumetricsShader.SetVector("_CamPos", new Vector4(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z, 0.0f));
			volumetricsShader.SetVector("_FogColor", fogColor);
			volumetricsShader.SetVector("_MainLightColor", sun.color);
			volumetricsShader.SetVector("_LightDirection", sun.transform.forward);
			volumetricsShader.SetVector("_LightContribution", lightContribution);
			volumetricsShader.SetFloat("_Time", Time.time);
			volumetricsShader.SetFloat("_DensityMultiplier", densityMultiplier);
			volumetricsShader.SetFloat("_DensityThreshold", densityThreshold);
			volumetricsShader.SetFloat("_LightScattering", lightScattering);
			volumetricsShader.SetFloat("_StepSize", stepSize);
			volumetricsShader.SetFloat("_NoiseSize", noiseSize);
			volumetricsShader.SetFloat("_YMin", yMin);
			volumetricsShader.SetFloat("_YMax", yMax);
			volumetricsShader.SetFloat("_MaxDistance", maxDist);
			
			var proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
			var view = cam.worldToCameraMatrix;
			proj[1, 1] = -proj[1, 1];
			var vp   = proj * view;
			volumetricsShader.SetMatrix("_InvVP", vp.inverse);
			
			volumetricsShader.Dispatch(0, volumetricsRenderTexture.width / 8, volumetricsRenderTexture.height / 8, 1);
			
			if (onRawImage) {
				rawImage.texture = volumetricsRenderTexture;
			}
		}
	}
}
