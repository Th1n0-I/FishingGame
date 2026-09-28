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

				image.texture = null;
			} else {
				image.material = null;

				image.texture = newTex;
			}
			image.SetAllDirty();
		}
	}
}