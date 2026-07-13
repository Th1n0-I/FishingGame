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
	[SerializeField]
	private int textureDivide = 2;
	[SerializeField] private float stepSize            = 1.0f;
	[SerializeField] private int   firstPassStepAmount = 1;
	[SerializeField] private int   stepAmount          = 1;
	[SerializeField] private float maxDist             = 100;
	[SerializeField] private bool  firstPass           = false;
	[SerializeField] private bool  useStepSize         = false;

	[Header("-Settings")]
	[SerializeField] private float fullDensityMult;
	[SerializeField] private float densityMultiplier = 1.0f;
	[SerializeField] private float fbmMult          = 1.0f;
	[SerializeField] private float densityThreshold = 1.0f;
	[SerializeField] private float lightScattering  = 1.0f;
	[SerializeField] private float noiseSize        = 1.0f;
	[SerializeField] private float detailSize       = 1.0f;
	[SerializeField] private float detailStrength   = 1.0f;
	[SerializeField] private float detail1Weight;
	[SerializeField] private float detail2Weight;
	[SerializeField] private float detail3Weight;
	[SerializeField] private float smallDetailStrength;
	[SerializeField] private float smallDetail1Weight;
	[SerializeField] private float smallDetail2Weight;
	[SerializeField] private float smallDetail3Weight;
	[SerializeField] private float yMin          = 60;
	[SerializeField] private float yMax          = 70;
	[SerializeField] private float shadowDensity = 1.0f;
	[SerializeField] private float gradient      = 0.2f;
	[SerializeField] private float squishFactor  = 2f;
	[SerializeField] private bool  useBoundingSphere;
	[SerializeField] private float sphereMinRadius = 1.0f;
	[SerializeField] private float sphereMaxRadius = 1.0f;
	[SerializeField] private float cumulusYMin     = 1500f;
	[SerializeField] private float cumulusYMax     = 2500f;
	[SerializeField] private bool  combineBounds   = true;
	[SerializeField] private Color fogColor;
	[ColorUsage(true, true)] [SerializeField]
	private Color lightContribution;
	[SerializeField] private Texture2D coverageTexture;
	[SerializeField] private Collider  bounds;
	[SerializeField] private Transform sphereCenter;

	[Header("Noise")]
	[SerializeField] private bool regenerateNoise = false;
	[Header("-Settings")]
	[Header("--Perlin-Worley")]
	[SerializeField, Range(0, 128)]
	private float pWPerlin1Size = 1.0f;
	[SerializeField, Range(0, 128)] private float pWPerlin2Size   = 1.0f;
	[SerializeField, Range(0, 128)] private float pWPerlin3Size   = 1.0f;
	[SerializeField, Range(0, 1)]   private float pWPerlin1Weight = 1.0f;
	[SerializeField, Range(0, 1)]   private float pWPerlin2Weight = 1.0f;
	[SerializeField, Range(0, 1)]   private float pWPerlin3Weight = 1.0f;
	[SerializeField, Range(0, 128)] private float pWWorley1Size   = 1.0f;
	[SerializeField, Range(0, 128)] private float pWWorley2Size   = 1.0f;
	[SerializeField, Range(0, 128)] private float pWWorley3Size   = 1.0f;
	[SerializeField, Range(0, 1)]   private float pWWorley1Weight = 1.0f;
	[SerializeField, Range(0, 1)]   private float pWWorley2Weight = 1.0f;
	[SerializeField, Range(0, 1)]   private float pWWorley3Weight = 1.0f;
	[Header("--Texture 1 Worley 1")]
	[SerializeField, Range(0, 128)] private float t1W1O1Size = 1.0f;
	[SerializeField, Range(0, 128)] private float t1W1O2Size   = 1.0f;
	[SerializeField, Range(0, 128)] private float t1W1O3Size   = 1.0f;
	[SerializeField, Range(0, 1)]   private float t1W1O1Weight = 1.0f;
	[SerializeField, Range(0, 1)]   private float t1W1O2Weight = 1.0f;
	[SerializeField, Range(0, 1)]   private float t1W1O3Weight = 1.0f;
	[Header("--Texture 1 Worley 2")]
	[SerializeField, Range(0, 128)] private float t1W2O1Size = 1.0f;
	[SerializeField, Range(0, 128)] private float t1W2O2Size   = 1.0f;
	[SerializeField, Range(0, 128)] private float t1W2O3Size   = 1.0f;
	[SerializeField, Range(0, 1)]   private float t1W2O1Weight = 1.0f;
	[SerializeField, Range(0, 1)]   private float t1W2O2Weight = 1.0f;
	[SerializeField, Range(0, 1)]   private float t1W2O3Weight = 1.0f;
	[Header("--Texture 1 Worley 3")]
	[SerializeField, Range(0, 128)] private float t1W3O1Size = 1.0f;
	[SerializeField, Range(0, 128)] private float t1W3O2Size   = 1.0f;
	[SerializeField, Range(0, 128)] private float t1W3O3Size   = 1.0f;
	[SerializeField, Range(0, 1)]   private float t1W3O1Weight = 1.0f;
	[SerializeField, Range(0, 1)]   private float t1W3O2Weight = 1.0f;
	[SerializeField, Range(0, 1)]   private float t1W3O3Weight = 1.0f;
	[Header("--Texture 2 Worley")]
	[SerializeField, Range(0, 128)] private float t2W1Size = 1.0f;
	[SerializeField, Range(0, 128)] private float t2W2Size = 1.0f;
	[SerializeField, Range(0, 128)] private float t2W3Size = 1.0f;


	[Header("Other")]
	[SerializeField] private ComputeShader noiseShader, volumetricsShader;

	[SerializeField] private RenderTexture perlinRenderTexture, worleyRenderTexture, volumetricsRenderTexture;
	private                  Light         sun;
	

	#region Caches

	private static readonly int PerlinTex           = Shader.PropertyToID("PerlinTex");
	private static readonly int WorleyTex           = Shader.PropertyToID("WorleyTex");
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
	private static readonly int PwPerlin1Size       = Shader.PropertyToID("pw_perlin1_size");
	private static readonly int PwPerlin2Size       = Shader.PropertyToID("pw_perlin2_size");
	private static readonly int PwPerlin3Size       = Shader.PropertyToID("pw_perlin3_size");
	private static readonly int PwPerlin1Weight     = Shader.PropertyToID("pw_perlin1_weight");
	private static readonly int PwPerlin2Weight     = Shader.PropertyToID("pw_perlin2_weight");
	private static readonly int PwPerlin3Weight     = Shader.PropertyToID("pw_perlin3_weight");
	private static readonly int PwWorley1Size       = Shader.PropertyToID("pw_worley1_size");
	private static readonly int PwWorley2Size       = Shader.PropertyToID("pw_worley2_size");
	private static readonly int PwWorley3Size       = Shader.PropertyToID("pw_worley3_size");
	private static readonly int PwWorley1Weight     = Shader.PropertyToID("pw_worley1_weight");
	private static readonly int PwWorley2Weight     = Shader.PropertyToID("pw_worley2_weight");
	private static readonly int PwWorley3Weight     = Shader.PropertyToID("pw_worley3_weight");
	private static readonly int T1W1O1Size          = Shader.PropertyToID("t1_w1_o1_size");
	private static readonly int T1W1O2Size          = Shader.PropertyToID("t1_w1_o2_size");
	private static readonly int T1W1O3Size          = Shader.PropertyToID("t1_w1_o3_size");
	private static readonly int T1W1O1Weight        = Shader.PropertyToID("t1_w1_o1_weight");
	private static readonly int T1W1O2Weight        = Shader.PropertyToID("t1_w1_o2_weight");
	private static readonly int T1W1O3Weight        = Shader.PropertyToID("t1_w1_o3_weight");
	private static readonly int T1W2O1Size          = Shader.PropertyToID("t1_w2_o1_size");
	private static readonly int T1W2O2Size          = Shader.PropertyToID("t1_w2_o2_size");
	private static readonly int T1W2O3Size          = Shader.PropertyToID("t1_w2_o3_size");
	private static readonly int T1W2O1Weight        = Shader.PropertyToID("t1_w2_o1_weight");
	private static readonly int T1W2O2Weight        = Shader.PropertyToID("t1_w2_o2_weight");
	private static readonly int T1W2O3Weight        = Shader.PropertyToID("t1_w2_o3_weight");
	private static readonly int T1W3O1Size          = Shader.PropertyToID("t1_w3_o1_size");
	private static readonly int T1W3O2Size          = Shader.PropertyToID("t1_w3_o2_size");
	private static readonly int T1W3O3Size          = Shader.PropertyToID("t1_w3_o3_size");
	private static readonly int T1W3O1Weight        = Shader.PropertyToID("t1_w3_o1_weight");
	private static readonly int T1W3O2Weight        = Shader.PropertyToID("t1_w3_o2_weight");
	private static readonly int T1W3O3Weight        = Shader.PropertyToID("t1_w3_o3_weight");
	private static readonly int T2W1Size            = Shader.PropertyToID("t2_w1_size");
	private static readonly int T2W2Size            = Shader.PropertyToID("t2_w2_size");
	private static readonly int T2W3Size            = Shader.PropertyToID("t2_w3_size");
	private static readonly int CumulusYMin         = Shader.PropertyToID("cumulus_y_min");
	private static readonly int CumulusYMax         = Shader.PropertyToID("cumulus_y_max");
	private static readonly int CombineBounds       = Shader.PropertyToID("combine_bounds");
	private static readonly int DetailFbmMult       = Shader.PropertyToID("detail_fbm_mult");
	private static readonly int DetailFbmWeight1    = Shader.PropertyToID("detail_fbm_weight1");
	private static readonly int DetailFbmWeight2    = Shader.PropertyToID("detail_fbm_weight2");
	private static readonly int DetailFbmWeight3    = Shader.PropertyToID("detail_fbm_weight3");
	private static readonly int DetailMult          = Shader.PropertyToID("detail_mult");
	private static readonly int DetailWeight1       = Shader.PropertyToID("detail_weight1");
	private static readonly int DetailWeight2       = Shader.PropertyToID("detail_weight2");
	private static readonly int DetailWeight3       = Shader.PropertyToID("detail_weight3");
	private static readonly int FullDensityMult     = Shader.PropertyToID("full_density_mult");

	#endregion

	#region Unity Functions

	private void Start() {
		InitializeNoise();

		InitializeVolumetrics();
	}

	private void Update() {
		DispatchVolumetrics();
		if (!regenerateNoise) return;
		DispatchNoise();
		regenerateNoise = false;
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
		noiseShader.SetFloat(PwPerlin1Size,   pWPerlin1Size);
		noiseShader.SetFloat(PwPerlin2Size,   pWPerlin2Size);
		noiseShader.SetFloat(PwPerlin3Size,   pWPerlin3Size);
		noiseShader.SetFloat(PwPerlin1Weight, pWPerlin1Weight);
		noiseShader.SetFloat(PwPerlin2Weight, pWPerlin2Weight);
		noiseShader.SetFloat(PwPerlin3Weight, pWPerlin3Weight);

		noiseShader.SetFloat(PwWorley1Size,   pWWorley1Size);
		noiseShader.SetFloat(PwWorley2Size,   pWWorley2Size);
		noiseShader.SetFloat(PwWorley3Size,   pWWorley3Size);
		noiseShader.SetFloat(PwWorley1Weight, pWWorley1Weight);
		noiseShader.SetFloat(PwWorley2Weight, pWWorley2Weight);
		noiseShader.SetFloat(PwWorley3Weight, pWWorley3Weight);

		noiseShader.SetFloat(T1W1O1Size,   t1W1O1Size);
		noiseShader.SetFloat(T1W1O2Size,   t1W1O2Size);
		noiseShader.SetFloat(T1W1O3Size,   t1W1O3Size);
		noiseShader.SetFloat(T1W1O1Weight, t1W1O1Weight);
		noiseShader.SetFloat(T1W1O2Weight, t1W1O2Weight);
		noiseShader.SetFloat(T1W1O3Weight, t1W1O3Weight);

		noiseShader.SetFloat(T1W2O1Size,   t1W2O1Size);
		noiseShader.SetFloat(T1W2O2Size,   t1W2O2Size);
		noiseShader.SetFloat(T1W2O3Size,   t1W2O3Size);
		noiseShader.SetFloat(T1W2O1Weight, t1W2O1Weight);
		noiseShader.SetFloat(T1W2O2Weight, t1W2O2Weight);
		noiseShader.SetFloat(T1W2O3Weight, t1W2O3Weight);

		noiseShader.SetFloat(T1W3O1Size,   t1W3O1Size);
		noiseShader.SetFloat(T1W3O2Size,   t1W3O2Size);
		noiseShader.SetFloat(T1W3O3Size,   t1W3O3Size);
		noiseShader.SetFloat(T1W3O1Weight, t1W3O1Weight);
		noiseShader.SetFloat(T1W3O2Weight, t1W3O2Weight);
		noiseShader.SetFloat(T1W3O3Weight, t1W3O3Weight);

		noiseShader.SetFloat(T2W1Size, t2W1Size);
		noiseShader.SetFloat(T2W2Size, t2W2Size);
		noiseShader.SetFloat(T2W3Size, t2W3Size);

		noiseShader.Dispatch(0, perlinRenderTexture.width    / 8, perlinRenderTexture.height / 8,
		                     perlinRenderTexture.volumeDepth / 8);
		noiseShader.Dispatch(1, worleyRenderTexture.width    / 8, worleyRenderTexture.height / 8,
		                     worleyRenderTexture.volumeDepth / 8);
	}

	private void InitializeWorley() {
		worleyRenderTexture = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		worleyRenderTexture.dimension = TextureDimension.Tex3D;
		worleyRenderTexture.volumeDepth = 32;
		worleyRenderTexture.enableRandomWrite = true;
		worleyRenderTexture.wrapMode = TextureWrapMode.Repeat;
		worleyRenderTexture.Create();
		Shader.SetGlobalTexture(WorleyTex1, worleyRenderTexture);
	}

	private void InitializePerlin() {
		perlinRenderTexture = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		perlinRenderTexture.dimension = TextureDimension.Tex3D;
		perlinRenderTexture.volumeDepth = 128;
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
		volumetricsRenderTexture = new RenderTexture(Screen.width / textureDivide, Screen.height / textureDivide, 0,
		                                             RenderTextureFormat.ARGBFloat);
		volumetricsRenderTexture.enableRandomWrite = true;
		volumetricsRenderTexture.wrapMode          = TextureWrapMode.Repeat;
		volumetricsRenderTexture.filterMode        = FilterMode.Bilinear;
		volumetricsRenderTexture.Create();
		Shader.SetGlobalTexture(VolumetricsTex, volumetricsRenderTexture);
	}

	private void DispatchVolumetrics() {
		var cam      = Camera.main;
		var depthTex = PersistentDepthFeature.PersistentDepthTexture;

		if (depthTex != null && depthTex.rt && cam) {
			volumetricsShader.SetTexture(0, DepthTex, depthTex.rt);

			volumetricsShader.SetVector(CamPos,
			                            new Vector4(cam.transform.position.x, cam.transform.position.y,
			                                        cam.transform.position.z, 0.0f));
			volumetricsShader.SetVector(FogColor,          fogColor);
			volumetricsShader.SetVector(MainLightColor,    sun.color);
			volumetricsShader.SetVector(LightDirection,    sun.transform.forward);
			volumetricsShader.SetVector(LightContribution, lightContribution);
			volumetricsShader.SetVector(MinBounds,         bounds.bounds.min);
			volumetricsShader.SetVector(MaxBounds,         bounds.bounds.max);
			volumetricsShader.SetVector(SphereCenter,
			                            new Vector4(sphereCenter.position.x, sphereCenter.position.y,
			                                        sphereCenter.position.z, 0.0f));
			
			volumetricsShader.SetFloat(DensityMultiplier, densityMultiplier);
			volumetricsShader.SetFloat(DensityThreshold,  densityThreshold);
			volumetricsShader.SetFloat(LightScattering,   lightScattering);
			volumetricsShader.SetFloat(NoiseSize,         noiseSize);
			volumetricsShader.SetFloat(DetailSize,        detailSize);
			volumetricsShader.SetFloat(MaxDistance,       maxDist);
			volumetricsShader.SetFloat(ShadowDensity,     shadowDensity);
			volumetricsShader.SetFloat(Gradient1,         gradient);
			volumetricsShader.SetFloat(SphereMinRadius,   sphereMinRadius);
			volumetricsShader.SetFloat(SphereMaxRadius,   sphereMaxRadius);
			volumetricsShader.SetFloat(SquishFactor,      squishFactor);
			volumetricsShader.SetFloat(CumulusYMin,       cumulusYMin);
			volumetricsShader.SetFloat(CumulusYMax,       cumulusYMax);
			volumetricsShader.SetFloat(DetailFbmMult,     detailStrength);
			volumetricsShader.SetFloat(DetailFbmWeight1,  detail1Weight);
			volumetricsShader.SetFloat(DetailFbmWeight2,  detail2Weight);
			volumetricsShader.SetFloat(DetailFbmWeight3,  detail3Weight);
			volumetricsShader.SetFloat(DetailMult,        smallDetailStrength);
			volumetricsShader.SetFloat(DetailWeight1,     smallDetail1Weight);
			volumetricsShader.SetFloat(DetailWeight2,     smallDetail2Weight);
			volumetricsShader.SetFloat(DetailWeight3,     smallDetail3Weight);
			volumetricsShader.SetFloat(FullDensityMult, fullDensityMult);
			
			volumetricsShader.SetInt(StepAmount,          math.max(stepAmount, 1));
			
			volumetricsShader.SetBool(UseStepSize,       useStepSize);
			volumetricsShader.SetBool(UseBoundingSphere, useBoundingSphere);
			volumetricsShader.SetBool(CombineBounds, combineBounds);

			var proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
			var view = cam.worldToCameraMatrix;
			proj[1, 1] = -proj[1, 1];
			var vp = proj * view;
			volumetricsShader.SetMatrix(InvVp, vp.inverse);

			volumetricsShader.Dispatch(0, volumetricsRenderTexture.width / 8, volumetricsRenderTexture.height / 8, 1);
		}
	}

	#endregion

	#endregion
}