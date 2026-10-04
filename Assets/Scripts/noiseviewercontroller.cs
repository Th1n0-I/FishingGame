using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class noiseviewercontroller : MonoBehaviour {
	[SerializeField] private NoiseController noiseController;
	[SerializeField] private Textures        textures = Textures.None;
	[SerializeField] private Material        sliceMat;
	private                  Textures        lastTexture = Textures.None;
	private                  RawImage        image;

	private enum Textures {
		None,
		Perlin,
		Worley,
		Weather,
		Shadow
	}

	private void Start() {
		image = GetComponent<RawImage>();
	}

	// Update is called once per frame
	void Update() {
		CheckTextureUpdate();
	}

	private void CheckTextureUpdate() {
		if (lastTexture != textures) {
			lastTexture = textures;

			Texture newTex = null;
			switch (textures) {
				case Textures.Perlin:
					newTex = noiseController.GetTexture(0);
					Debug.Log(newTex);
					break;
				case Textures.Worley:
					newTex = noiseController.GetTexture(1);
					Debug.Log(newTex);
					break;
				case Textures.Weather:
					newTex = noiseController.GetTexture(2);
					Debug.Log(newTex);
					break;
				case Textures.Shadow:
					newTex = noiseController.GetTexture(3);
					Debug.Log(newTex);
					break;
				default:
					newTex = null;
					break;
			}
			if (!newTex) {
				image.material = null;
				image.texture  = null;
				return;
			}
			Debug.Log(newTex.width + " " + newTex.dimension);
			if (newTex.dimension == TextureDimension.Tex3D) {
				image.material = sliceMat;
				sliceMat.SetTexture("_Volume", newTex);
				sliceMat.SetFloat("_UseFlat", 0);

				image.texture = null;
			} else {
				// Through the slice material too, which shows it without its alpha. The weather map's alpha holds the
				// cirrus wisps, which made the preview see-through.
				image.material = sliceMat;
				sliceMat.SetTexture("_Flat", newTex);
				sliceMat.SetFloat("_UseFlat", 1);

				image.texture = null;
			}
			image.SetAllDirty();
		}
	}
}