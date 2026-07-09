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
	[Header("-Quality")]
	[SerializeField] private int textureDivide = 2;
	[SerializeField] private float stepSize            = 1.0f;
	[SerializeField] private int   firstPassStepAmount = 1;
	[SerializeField] private int   stepAmount          = 1;
	[SerializeField] private float maxDist             = 100;
	[SerializeField] private bool  firstPass           = false;
	[SerializeField] private bool  useStepSize         = false;
	
	[Header("-Settings")]
	[SerializeField] private float densityMultiplier   = 1.0f;
	[SerializeField] private float fbmMult          = 1.0f;
	[SerializeField] private float densityThreshold = 1.0f;
	[SerializeField] private float lightScattering  = 1.0f;
	[SerializeField] private float noiseSize        = 1.0f;
	[SerializeField] private float detailSize       = 1.0f;
	[SerializeField] private float detailStrength   = 1.0f;
	[SerializeField] private float yMin             = 60;
	[SerializeField] private float yMax             = 70;
	[SerializeField] private float shadowDensity    = 1.0f;
	[SerializeField] private float gradient         = 0.2f;
	[SerializeField] private float squishFactor     = 2f;
	[SerializeField] private bool  useBoundingSphere;
	[SerializeField] private float sphereMinRadius = 1.0f;
	[SerializeField] private float sphereMaxRadius = 1.0f;
	[SerializeField] private Color fogColor;
	[ColorUsage(true, true)]
	[SerializeField] private Color lightContribution;
	[SerializeField] private Texture2D coverageTexture;
	[SerializeField] private Collider  bounds;
	[SerializeField] private Transform sphereCenter;
	
	[Header("Other")]
	[SerializeField]             private ComputeShader noiseShader,         volumetricsShader;
	
	[SerializeField] private RenderTexture perlinRenderTexture, worleyRenderTexture,volumetricsRenderTexture;
	private Light         sun;
	
	#region Caches
	
	private static readonly int PerlinTex           = Shader.PropertyToID("PerlinTex");
	private static readonly int WorleyTex           = Shader.PropertyToID("WorleyTex");
	private static readonly int WorleySize1         = Shader.PropertyToID("_WorleySize1");
	private static readonly int WorleySize2         = Shader.PropertyToID("_WorleySize2");
	private static readonly int WorleySize3         = Shader.PropertyToID("_WorleySize3");
	private static readonly int WorleySize4         = Shader.PropertyToID("_WorleySize4");
	private static readonly int PerlinSize1         = Shader.PropertyToID("_PerlinSize1");
	private static readonly int PerlinSize2         = Shader.PropertyToID("_PerlinSize2");
	private static readonly int PerlinSize3         = Shader.PropertyToID("_PerlinSize3");
	private static readonly int PerlinSize4         = Shader.PropertyToID("_PerlinSize4");
	private static readonly int WorleyTex1          = Shader.PropertyToID("_WorleyTex");
	private static readonly int PerlinTex1          = Shader.PropertyToID("_PerlinTex");
	private static readonly int TextureDivide       = Shader.PropertyToID("TextureDivide");
	private static readonly int ScreenWidth         = Shader.PropertyToID("ScreenWidth");
	private static readonly int ScreenHeight        = Shader.PropertyToID("ScreenHeight");
	private static readonly int Result              = Shader.PropertyToID("Result");
	private static readonly int DepthTex            = Shader.PropertyToID("DepthTex");
	private static readonly int VolumetricsTex      = Shader.PropertyToID("_VolumetricsTex");
	private static readonly int CamPos              = Shader.PropertyToID("_CamPos");
	private static readonly int FogColor            = Shader.PropertyToID("_FogColor");
	private static readonly int MainLightColor      = Shader.PropertyToID("_MainLightColor");
	private static readonly int LightDirection      = Shader.PropertyToID("_LightDirection");
	private static readonly int LightContribution   = Shader.PropertyToID("_LightContribution");
	private static readonly int MinBounds           = Shader.PropertyToID("_MinBounds");
	private static readonly int MaxBounds           = Shader.PropertyToID("_MaxBounds");
	private static readonly int Time1               = Shader.PropertyToID("_Time");
	private static readonly int DensityMultiplier   = Shader.PropertyToID("_DensityMultiplier");
	private static readonly int DensityThreshold    = Shader.PropertyToID("_DensityThreshold");
	private static readonly int LightScattering     = Shader.PropertyToID("_LightScattering");
	private static readonly int StepSize            = Shader.PropertyToID("_StepSize");
	private static readonly int NoiseSize           = Shader.PropertyToID("_NoiseSize");
	private static readonly int DetailSize          = Shader.PropertyToID("_DetailSize");
	private static readonly int DetailStrength      = Shader.PropertyToID("_DetailStrength");
	private static readonly int MaxDistance         = Shader.PropertyToID("_MaxDistance");
	private static readonly int ShadowDensity       = Shader.PropertyToID("_ShadowDensity");
	private static readonly int Gradient1           = Shader.PropertyToID("_Gradient");
	private static readonly int FbmMult             = Shader.PropertyToID("_FBMMult");
	private static readonly int FirstPassStepAmount = Shader.PropertyToID("_FirstPassStepAmount");
	private static readonly int StepAmount          = Shader.PropertyToID("_StepAmount");
	private static readonly int FirstPass           = Shader.PropertyToID("_FirstPass");
	private static readonly int UseStepSize         = Shader.PropertyToID("_UseStepSize");
	private static readonly int InvVp               = Shader.PropertyToID("_InvVP");
	private static readonly int SphereMinRadius     = Shader.PropertyToID("_SphereMinRadius");
	private static readonly int SphereMaxRadius     = Shader.PropertyToID("_SphereMaxRadius");
	private static readonly int SphereCenter        = Shader.PropertyToID("_SphereCenter");
	private static readonly int UseBoundingSphere   = Shader.PropertyToID("_UseBoundingSphere");
	private static readonly int SquishFactor        = Shader.PropertyToID("_SquishFactor");

	#endregion

	#region Unity Functions
	
	private void Start() {
		
		InitializeNoise();
		
		InitializeVolumetrics();
	}

	private void Update() {
		DispatchVolumetrics();
	}
	
	#endregion
	
	#region Custom Functions
	#region Noise Functions
	private void InitializeNoise() {
		InitializeWorley();
		InitializePerlin();

		noiseShader.SetTexture(0, PerlinTex, perlinRenderTexture);
		noiseShader.SetTexture(1, WorleyTex, worleyRenderTexture);
		
		DispatchNoise();
	}

	
	private void DispatchNoise() {
		noiseShader.SetFloat(WorleySize1, 8);
		noiseShader.SetFloat(WorleySize2, 8);
		noiseShader.SetFloat(WorleySize3, 16);
		noiseShader.SetFloat(WorleySize4, 8 );
		
		noiseShader.SetFloat(PerlinSize1, 32);
		noiseShader.SetFloat(PerlinSize2, 64);
		noiseShader.SetFloat(PerlinSize3, 128);
		noiseShader.SetFloat(PerlinSize4, 16);
		
		noiseShader.Dispatch(0, perlinRenderTexture.width / 8, perlinRenderTexture.height / 8, perlinRenderTexture.volumeDepth / 8);
		noiseShader.Dispatch(1,worleyRenderTexture.width / 8, worleyRenderTexture.height / 8, worleyRenderTexture.volumeDepth / 8);
	}
	
	private void InitializeWorley() {
		worleyRenderTexture                   = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		worleyRenderTexture.dimension         = TextureDimension.Tex3D;
		worleyRenderTexture.volumeDepth       = 32;
		worleyRenderTexture.enableRandomWrite = true;
		worleyRenderTexture.wrapMode          = TextureWrapMode.Repeat;
		worleyRenderTexture.Create();
		Shader.SetGlobalTexture(WorleyTex1, worleyRenderTexture);
	}
	
	private void InitializePerlin() {
		perlinRenderTexture                   = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		perlinRenderTexture.dimension         = TextureDimension.Tex3D;
		perlinRenderTexture.volumeDepth       = 128;
		perlinRenderTexture.enableRandomWrite = true;
		perlinRenderTexture.wrapMode = TextureWrapMode.Repeat;
		perlinRenderTexture.Create();
		Shader.SetGlobalTexture(PerlinTex1, perlinRenderTexture);
	}
	#endregion
	
	#region Volumetrics Functions

	private void InitializeVolumetrics() {
		
		sun = RenderSettings.sun;
		
		volumetricsShader.SetInt(TextureDivide, textureDivide);
		volumetricsShader.SetInt(ScreenWidth,   Screen.width);
		volumetricsShader.SetInt(ScreenHeight,  Screen.height);
		
		CreateVolumetricTexture();
		
		volumetricsShader.SetTexture(0, PerlinTex1, perlinRenderTexture);
		volumetricsShader.SetTexture(0, WorleyTex1, worleyRenderTexture);
		volumetricsShader.SetTexture(0, Result,     volumetricsRenderTexture);
	}

	private void CreateVolumetricTexture() {
		volumetricsRenderTexture = new RenderTexture(Screen.width / textureDivide, Screen.height / textureDivide, 0, RenderTextureFormat.ARGBFloat);
		volumetricsRenderTexture.enableRandomWrite = true;
		volumetricsRenderTexture.wrapMode = TextureWrapMode.Repeat;
		volumetricsRenderTexture.filterMode = FilterMode.Bilinear;
		volumetricsRenderTexture.Create();
		Shader.SetGlobalTexture(VolumetricsTex, volumetricsRenderTexture);
	}

	private void DispatchVolumetrics() {
		var cam = Camera.main;
		var depthTex = PersistentDepthFeature.PersistentDepthTexture;
		
		if (depthTex != null && depthTex.rt && cam) {
			volumetricsShader.SetTexture(0, DepthTex,   depthTex.rt);
			
			volumetricsShader.SetVector(CamPos, new Vector4(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z, 0.0f));
			volumetricsShader.SetVector(FogColor, fogColor);
			volumetricsShader.SetVector(MainLightColor, sun.color);
			volumetricsShader.SetVector(LightDirection, sun.transform.forward);
			volumetricsShader.SetVector(LightContribution, lightContribution);
			volumetricsShader.SetVector(MinBounds, bounds.bounds.min);
			volumetricsShader.SetVector(MaxBounds, bounds.bounds.max);
			volumetricsShader.SetVector(SphereCenter, new Vector4(sphereCenter.position.x, sphereCenter.position.y, sphereCenter.position.z, 0.0f));
			
			volumetricsShader.SetFloat(Time1,              Time.time);
			volumetricsShader.SetFloat(DensityMultiplier,  densityMultiplier);
			volumetricsShader.SetFloat(DensityThreshold,   densityThreshold);
			volumetricsShader.SetFloat(LightScattering,    lightScattering);
			volumetricsShader.SetFloat(StepSize,           math.max(0.1f, stepSize));
			volumetricsShader.SetFloat(NoiseSize,          noiseSize);
			volumetricsShader.SetFloat(DetailSize,         detailSize);
			volumetricsShader.SetFloat(DetailStrength,     detailStrength);
			volumetricsShader.SetFloat(MaxDistance,        maxDist);
			volumetricsShader.SetFloat(ShadowDensity,      shadowDensity);
			volumetricsShader.SetFloat(Gradient1,          gradient);
			volumetricsShader.SetFloat(FbmMult,            fbmMult);
			volumetricsShader.SetFloat(SphereMinRadius, sphereMinRadius);
			volumetricsShader.SetFloat(SphereMaxRadius, sphereMaxRadius);
			volumetricsShader.SetFloat(SquishFactor, squishFactor);
			
			volumetricsShader.SetInt(FirstPassStepAmount, firstPassStepAmount);
			volumetricsShader.SetInt(StepAmount,        math.min(math.max(stepAmount, 1),100));

			volumetricsShader.SetBool(FirstPass, firstPass);
			volumetricsShader.SetBool(UseStepSize, useStepSize);
			volumetricsShader.SetBool(UseBoundingSphere, useBoundingSphere);
			
			var proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
			var view = cam.worldToCameraMatrix;
			proj[1, 1] = -proj[1, 1];
			var vp   = proj * view;
			volumetricsShader.SetMatrix(InvVp, vp.inverse);
			
			volumetricsShader.Dispatch(0, volumetricsRenderTexture.width / 8, volumetricsRenderTexture.height / 8, 1);
		}
	}
	#endregion
	
	
	#endregion
}
