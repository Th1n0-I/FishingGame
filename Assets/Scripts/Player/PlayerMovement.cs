using System;
using FishingGame;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player {
	public class PlayerMovement : MonoBehaviour {
		#region Fields

		private static DebugHandler Debug;

		[Header("Settings")]
		[SerializeField] private float maxSpeed, acceleration;
		[SerializeField] private float groundMaxDist;

		[Header("Components")]
		[SerializeField] private Transform groundCheckPosition;
		[SerializeField] private GameObject                     playerCamera;
		[SerializeField] private Canvas                         cloudCanvas;
		private                  Rigidbody                      playerRb;
		private                  CinemachineInputAxisController playerAxisController;

		//? Inputs
		private InputAction lookAction;
		private InputAction moveAction;
		private InputAction pauseAction;

		//? States
		private Vector3 moveVector;
		private Vector3 lookVector;
		private Vector3 velocity;

		private bool isPaused;

		//? Move these states
		private bool isGrounded;

		#endregion

		#region Unity Functions

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void OnRuntimeInit() {
			Debug = new DebugHandler("PlayerMovement");
		}

		private void Start() {
			Debug ??= new DebugHandler("PlayerMovement");

			playerRb = GetComponent<Rigidbody>();

			moveAction = InputSystem.actions.FindAction("Move");
			
			pauseAction = InputSystem.actions.FindAction("Pause");
			cloudCanvas.enabled = false;
			
			playerAxisController = playerCamera.GetComponent<CinemachineInputAxisController>();

			Cursor.lockState = CursorLockMode.Locked;
		}

		private void Update() {
			CheckPause();
			CheckGround();
		}

		private void FixedUpdate() {
			PerformMove();
		}

		private void OnDrawGizmos() {
			//? Ground Check
			Gizmos.color = Lib.Movement.GroundCheck(groundCheckPosition.position, groundMaxDist)
				               ? Color.green
				               : Color.red;
			Gizmos.DrawWireSphere(groundCheckPosition.position, groundMaxDist);
		}

		#endregion

		#region Functions

		private void CheckPause() {
			if (pauseAction.triggered) {
				switch (isPaused)
				{
					case true:
						isPaused = false;
						Cursor.lockState = CursorLockMode.Locked;
						cloudCanvas.enabled = false;
						playerAxisController.enabled = true;
						break;
					case false:
						isPaused = true;
						Cursor.lockState = CursorLockMode.None;
						cloudCanvas.enabled = true;
						playerAxisController.enabled = false;
						break;
				}
			}
		}

		private void CheckGround() => isGrounded = Lib.Movement.GroundCheck(transform.position, groundMaxDist);


		private void PerformMove() {
			var inputVector   = moveAction.ReadValue<Vector2>();
			var moveDirection = (transform.forward * inputVector.y + transform.right * inputVector.x).normalized;

			moveVector = moveDirection * maxSpeed;

			var playerVelocity  = new Vector3(playerRb.linearVelocity.x, 0, playerRb.linearVelocity.z);
			var speedDifference = moveVector - playerVelocity;
			var finalForce      = speedDifference * acceleration;

			playerRb.AddForce(finalForce, ForceMode.Force);

			Debug.LogKv("PerformMove", DebugLevel.Debug, new object[] {
				"moveVector", moveVector,
				"playerVelocity", playerVelocity,
				"speedDifference", speedDifference,
				"finalForce", finalForce
			});
		}

		#endregion
	}
}