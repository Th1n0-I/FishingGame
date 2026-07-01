using FishingGame;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player {
	public class PlayerMovement : MonoBehaviour {
		#region fields

		[Header("Settings")]
		[SerializeField] private float movementSpeed;

		//? Components
		private Rigidbody rb;

		//? Inputs
		private InputAction lookAction;
		private InputAction moveAction;

		//? States
		private Vector3 moveVector;
		private Vector3 lookVector;
		private Vector3 velocity;

		#endregion

		#region Unity functions

		private void Start() {
			rb = GetComponent<Rigidbody>();

			moveAction = InputSystem.actions.FindAction("Move");
			lookAction = InputSystem.actions.FindAction("Look");

			Cursor.lockState = CursorLockMode.Locked;
		}


		private void Update() => CheckPlayerInputs();

		#endregion

		#region custom functions

		private void CheckPlayerInputs() {
			lookVector            =  lookAction.ReadValue<Vector2>();
			transform.eulerAngles += new Vector3(0, lookVector.x, 0) * Preferences.Input.MouseSensitivity;


			moveVector = moveAction.ReadValue<Vector2>();
			velocity.x = (moveVector.y * transform.forward.x + moveVector.x * transform.right.x) * movementSpeed;
			velocity.z = (moveVector.y * transform.forward.z + moveVector.x * transform.right.z) * movementSpeed;
			velocity.y = rb.linearVelocity.y;

			rb.linearVelocity = velocity;
		}

		#endregion
	}
}