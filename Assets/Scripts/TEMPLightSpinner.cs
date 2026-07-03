using UnityEngine;

public class TEMPLightSpinner : MonoBehaviour {
	[SerializeField] private float rotationsPerSecond;
	private void Update() {
		transform.eulerAngles += new Vector3(0, rotationsPerSecond * Time.deltaTime * 360, 0);
	}
}
