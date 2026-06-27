using UnityEngine;

public class OceanHandler : MonoBehaviour
{
	#region Fields
	
	//? Settings
	[SerializeField] private int   oceanRad;
	[SerializeField] private int chunkSize;
	[SerializeField] private Material oceanMat;
	
	//? States
	private OceanChunk[,] chunks;
	
	//? Refs
	private Transform playerTransform;
	
	//? Meshes
	private Mesh lowDetailMesh;
	private Mesh mediumDetailMesh;
	private Mesh highDetailMesh;

	#endregion
	
	#region unityFunctions

	private void Start() {
		lowDetailMesh = LowDetailMesh.CreateMesh(chunkSize);
		mediumDetailMesh = MediumDetailMesh.CreateMesh(chunkSize);
		highDetailMesh = HighDetailMesh.CreateMesh(chunkSize);
		
		playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
		GenerateChunks();
	}

	private void Update() {
		UpdateChunks();
	}

	#endregion

	#region customFunctions

	private void GenerateChunks() {
		chunks = new OceanChunk[oceanRad * 2 + 1, oceanRad * 2 + 1];

		for (int x = -oceanRad; x <= oceanRad; x++) {
			for (int z = -oceanRad; z <= oceanRad; z++) {
				CreateChunk(x, z);
			}
		}
	}

	private void CreateChunk(int x, int z) {
		GameObject obj = new GameObject($"Chunk{x}_{z}");
		obj.transform.parent = transform;
		
		obj.transform.position = new Vector3(
		                                     x * chunkSize,
		                                     7,
		                                     z * chunkSize);
		
		MeshFilter mf = obj.AddComponent<MeshFilter>();
		obj.AddComponent<MeshRenderer>();
		obj.GetComponent<MeshRenderer>().material = oceanMat;
		
		OceanChunk chunk = new OceanChunk();
		chunk.Obj             = obj;
		chunk.MeshFilter      = mf;
		chunk.MeshFilter.mesh = lowDetailMesh;
		chunk.CurrentLOD = LODType.Low;
		
		chunks[x + oceanRad, z + oceanRad] = chunk;
	}

	private void UpdateChunks() {
		for (int x = 0; x < chunks.GetLength(0); x++) {
			for (int z = 0; z < chunks.GetLength(1); z++) {
				OceanChunk chunk = chunks[x, z];
				
				float dist = Vector3.Distance(playerTransform.position - new Vector3(chunkSize/2f, 0, chunkSize/2f), chunk.Obj.transform.position);

				if (dist < 60f) {
					SetLOD(chunk, LODType.High);
				} else if (dist < 150f) {
					SetLOD(chunk, LODType.Medium);
				} else {
					SetLOD(chunk, LODType.Low);
					
				}
			}
		}
	}

	private void SetLOD(OceanChunk chunk, LODType lod) {
		if (chunk.CurrentLOD == lod) return;
		
		chunk.CurrentLOD = lod;

		switch (lod) {
			case LODType.High:
				chunk.MeshFilter.mesh = highDetailMesh;
				break;
			case LODType.Medium:
				chunk.MeshFilter.mesh = mediumDetailMesh;
				break;
			case LODType.Low:
				chunk.MeshFilter.mesh = lowDetailMesh;
				break;
		}
	}

	#endregion
}

#region Classes

public class OceanChunk {
	public GameObject Obj;
	public MeshFilter MeshFilter;

	public LODType CurrentLOD;
}

public enum LODType {
	Low,
	Medium,
	High
}


public static class LowDetailMesh {
	public static Mesh CreateMesh(int chunkSize) {
		Mesh mesh = new Mesh();

		int       size                = 10;
		float       placementMultiplier = chunkSize / (size - 1f);
		Vector3[] vertices            = new Vector3[size   * size];
		int[]     triangles           = new int[(size - 1) * (size - 1) * 6];

		int i   = 0;
		int tri = 0;
		
		for (int z = 0; z < size; z++) {
			for (int x = 0; x < size; x++) {
				i = z * size + x;
				vertices[i] = new Vector3(x * placementMultiplier, 0, z * placementMultiplier);
			}
		}

		for (int x = 0; x < size - 1; x++) {
			for (int z = 0; z < size - 1; z++) {
				i =  z * size + x;

				triangles[tri] = i;
				triangles[tri + 1] = i + size;
				triangles[tri + 2] = i + 1;
				
				triangles[tri + 3] = i + 1;
				triangles[tri + 4] = i + size;
				triangles[tri + 5] = i + size + 1;

				tri += 6;
			}
		}
		
		mesh.vertices = vertices;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();

		return mesh;
	}
}

public static class MediumDetailMesh {
	public static Mesh CreateMesh(int chunkSize) {
		Mesh mesh = new Mesh();

		int       size                = 25;
		float       placementMultiplier = chunkSize / (size - 1f);
		Vector3[] vertices            = new Vector3[size   * size];
		int[]     triangles           = new int[(size - 1) * (size - 1) * 6];

		int i   = 0;
		int tri = 0;
		
		for (int z = 0; z < size; z++) {
			for (int x = 0; x < size; x++) {
				i           = z * size + x;
				vertices[i] = new Vector3(x * placementMultiplier, 0, z * placementMultiplier);
			}
		}

		for (int x = 0; x < size - 1; x++) {
			for (int z = 0; z < size - 1; z++) {
				i =  z * size + x;

				triangles[tri]     = i;
				triangles[tri + 1] = i + size;
				triangles[tri + 2] = i + 1;
				
				triangles[tri + 3] = i + 1;
				triangles[tri + 4] = i + size;
				triangles[tri + 5] = i + size + 1;

				tri += 6;
			}
		}
		
		mesh.vertices  = vertices;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();

		return mesh;
	}
}

public static class HighDetailMesh {
	public static Mesh CreateMesh(int chunkSize) {
		Mesh mesh = new Mesh();

		int       size                = 50;
		float       placementMultiplier = chunkSize / (size - 1f);
		Vector3[] vertices            = new Vector3[size   * size];
		int[]     triangles           = new int[(size - 1) * (size - 1) * 6];

		int i   = 0;
		int tri = 0;
		
		for (int z = 0; z < size; z++) {
			for (int x = 0; x < size; x++) {
				i           = z * size + x;
				vertices[i] = new Vector3(x * placementMultiplier, 0, z * placementMultiplier);
			}
		}

		for (int x = 0; x < size - 1; x++) {
			for (int z = 0; z < size - 1; z++) {
				i =  z * size + x;

				triangles[tri]     = i;
				triangles[tri + 1] = i + size;
				triangles[tri + 2] = i + 1;
				
				triangles[tri + 3] = i + 1;
				triangles[tri + 4] = i + size;
				triangles[tri + 5] = i + size + 1;

				tri += 6;
			}
		}
		
		mesh.vertices  = vertices;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();

		return mesh;
	}
}



#endregion
