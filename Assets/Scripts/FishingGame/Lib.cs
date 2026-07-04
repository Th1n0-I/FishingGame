using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace FishingGame {
	public abstract class Lib {
		#region Fields

		private static LayerMask GroundLayerMask => LayerMask.GetMask("Ground");

		#endregion

		public abstract class Movement {
			/// <summary>
			/// Check if the collider beneath the position is ground and is within maxDist
			/// </summary>
			/// <param name="position">Where to originate from</param>
			/// <param name="maxDist">Maximum distance to check</param>
			/// <returns>Collider that is hit, else null</returns>
			public static Collider GroundCheck(Vector3 position, float maxDist) =>
				Physics.Raycast(position, Vector3.down, out var hit, maxDist, GroundLayerMask) ? hit.collider : null;
		}
	}
}