using System.Collections.Generic;
using GrayWolf.GPUInstancing.Domain;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class NoiseController : MonoBehaviour {

	[Header("Volumetrics")]
	[SerializeField] private int textureDivide = 2;
	[SerializeField] private float densityMultiplier   = 1.0f;
	[SerializeField] private float fbmMult             = 1.0f;
	[SerializeField] private float densityThreshold    = 1.0f;
	[SerializeField] private float lightScattering     = 1.0f;
	[SerializeField] private float stepSize            = 1.0f;
	[SerializeField] private int   firstPassStepAmount = 1;
	[SerializeField] private int   stepAmount          = 1;
	[SerializeField] private float noiseSize           = 1.0f;
	[SerializeField] private float detailSize          = 1.0f;
	[SerializeField] private float detailStrength      = 1.0f;
	[SerializeField] private float yMin                = 60;
	[SerializeField] private float yMax                = 70;
	[SerializeField] private float maxDist             = 100;
	[SerializeField] private float shadowDensity       = 1.0f;
	[SerializeField] private float gradient            = 0.2f;
	[SerializeField] private bool  firstPass           = false;
	[SerializeField] private bool  useStepSize         = false;
	[SerializeField] private Color fogColor;
	[ColorUsage(true, true)]
	[SerializeField] private Color lightContribution;
	[SerializeField] private Texture2D coverageTexture;
	[SerializeField] private Collider  bounds;
	
	[Header("Other")]
	[SerializeField] private int seed = 0;
	[SerializeField]             private ComputeShader noiseShader,         volumetricsShader;
	[SerializeField]         private RenderTexture perlinRenderTexture, worleyRenderTexture,volumetricsRenderTexture;
	[SerializeField]             private bool          onRawImage;
	[SerializeField]             private int           dotAmount;
	[SerializeField, Range(0,1)] private float         z;
	
	private RawImage rawImage;

	private void OnEnable() {
		RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
	}

	private void OnDisable() {
		RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
	}

	private void Start() {
		perlinRenderTexture                   = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		perlinRenderTexture.dimension         = TextureDimension.Tex3D;
		perlinRenderTexture.volumeDepth       = 128;
		perlinRenderTexture.enableRandomWrite = true;
		perlinRenderTexture.wrapMode = TextureWrapMode.Repeat;
		perlinRenderTexture.Create();
		
		worleyRenderTexture                   = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		worleyRenderTexture.dimension         = TextureDimension.Tex3D;
		worleyRenderTexture.volumeDepth       = 32;
		worleyRenderTexture.enableRandomWrite = true;
		worleyRenderTexture.wrapMode          = TextureWrapMode.Repeat;
		worleyRenderTexture.Create();
		
		volumetricsRenderTexture = new RenderTexture(Screen.width / textureDivide, Screen.height / textureDivide, 0, RenderTextureFormat.ARGBFloat);
		volumetricsRenderTexture.enableRandomWrite = true;
		volumetricsRenderTexture.wrapMode = TextureWrapMode.Repeat;
		volumetricsRenderTexture.filterMode = FilterMode.Bilinear;
		volumetricsRenderTexture.Create();
		
		noiseShader.SetTexture(0, "PerlinTex", perlinRenderTexture);
		noiseShader.SetTexture(1, "WorleyTex", worleyRenderTexture);
		
		noiseShader.SetFloat("_WorleySize1", 8);
		noiseShader.SetFloat("_WorleySize2", 8);
		noiseShader.SetFloat("_WorleySize3", 16);
		noiseShader.SetFloat("_WorleySize4", 8);
		
		noiseShader.SetFloat("_PerlinSize1", 32);
		noiseShader.SetFloat("_PerlinSize2", 64);
		noiseShader.SetFloat("_PerlinSize3", 128);
		noiseShader.SetFloat("_PerlinSize4", 16);
		
		noiseShader.Dispatch(0, perlinRenderTexture.width / 8, perlinRenderTexture.height / 8, perlinRenderTexture.volumeDepth / 8);
		noiseShader.Dispatch(1,worleyRenderTexture.width / 8, worleyRenderTexture.height / 8, worleyRenderTexture.volumeDepth / 8);
		Shader.SetGlobalTexture("_PerlinTex", perlinRenderTexture);
		Shader.SetGlobalTexture("_WorleyTex", worleyRenderTexture);
		
		volumetricsShader.SetTexture(0, "_PerlinTex",       perlinRenderTexture);
		volumetricsShader.SetTexture(0, "_WorleyTex",       worleyRenderTexture);
		volumetricsShader.SetTexture(0, "Result",           volumetricsRenderTexture);
		volumetricsShader.SetTexture(0, "_CoverageTexture", coverageTexture);
		
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
			
			volumetricsShader.SetTexture(0, "NoiseTex", perlinRenderTexture);
			volumetricsShader.SetTexture(0, "DepthTex", depthTex.rt);
			
			volumetricsShader.SetVector("_CamPos", new Vector4(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z, 0.0f));
			volumetricsShader.SetVector("_FogColor", fogColor);
			volumetricsShader.SetVector("_MainLightColor", sun.color);
			volumetricsShader.SetVector("_LightDirection", sun.transform.forward);
			volumetricsShader.SetVector("_LightContribution", lightContribution);
			volumetricsShader.SetVector("_MinBounds", bounds.bounds.min);
			volumetricsShader.SetVector("_MaxBounds", bounds.bounds.max);
			volumetricsShader.SetFloat("_Time",              Time.time);
			volumetricsShader.SetFloat("_DensityMultiplier", densityMultiplier);
			volumetricsShader.SetFloat("_DensityThreshold",  densityThreshold);
			volumetricsShader.SetFloat("_LightScattering",   lightScattering);
			volumetricsShader.SetFloat("_StepSize",          math.max(0.1f, stepSize));
			volumetricsShader.SetFloat("_NoiseSize",         noiseSize);
			volumetricsShader.SetFloat("_DetailSize", detailSize);
			volumetricsShader.SetFloat("_DetailStrength", detailStrength);
			volumetricsShader.SetFloat("_YMin",              yMin);
			volumetricsShader.SetFloat("_YMax",              yMax);
			volumetricsShader.SetFloat("_MaxDistance",       maxDist);
			volumetricsShader.SetFloat("_ShadowDensity",     shadowDensity);
			volumetricsShader.SetFloat("_Gradient",          gradient);
			volumetricsShader.SetFloat("_FBMMult", fbmMult);
			
			volumetricsShader.SetInt("_FirstPassStepAmount", firstPassStepAmount);
			volumetricsShader.SetInt("_StepAmount",        math.min(math.max(stepAmount, 1),100));

			volumetricsShader.SetBool("_FirstPass", firstPass);
			volumetricsShader.SetBool("_UseStepSize", useStepSize);
			
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
