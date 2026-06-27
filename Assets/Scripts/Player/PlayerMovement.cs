using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour {
	#region fields

	[Header("Settings")]
	[SerializeField] private float maxPitch;
	[SerializeField] private float sensitivity;
	[SerializeField] private float movementSpeed;

	//? Components
	private Rigidbody  rb;
	private GameObject cam;
	private Transform  camTransform;
	
	//? Inputs
	private InputAction lookAction;
	private InputAction moveAction;

	//? States
	private Vector3 moveVector;
	private Vector3 lookVector;
	private Vector3 velocity;

	#endregion

	#region Unity functions

	void Start() {
		rb           = GetComponent<Rigidbody>();
		cam          = GameObject.Find("Main Camera");
		camTransform = cam.transform;
		
		moveAction = InputSystem.actions.FindAction("Move");
		lookAction = InputSystem.actions.FindAction("Look");

		Cursor.lockState = CursorLockMode.Locked;
	}


	void Update() => CheckPlayerInputs();

	#endregion

	#region custom functions

	private void CheckPlayerInputs() {
		lookVector               =  lookAction.ReadValue<Vector2>();
		camTransform.eulerAngles += new Vector3(-lookVector.y, 0,            0) * sensitivity;
		transform.eulerAngles    += new Vector3(0,             lookVector.x, 0) * sensitivity;

		if (camTransform.eulerAngles.x > maxPitch && camTransform.eulerAngles.x < 180)
			camTransform.localEulerAngles = new Vector3(maxPitch, 0, 0);
		if (camTransform.eulerAngles.x < 360 - maxPitch && camTransform.eulerAngles.x > 180)
			camTransform.localEulerAngles = new Vector3(-maxPitch, 0, 0);

		moveVector = moveAction.ReadValue<Vector2>();
		velocity.x = (moveVector.y * transform.forward.x + moveVector.x * transform.right.x) * movementSpeed;
		velocity.z = (moveVector.y * transform.forward.z + moveVector.x * transform.right.z) * movementSpeed;
		velocity.y = rb.linearVelocity.y;
		
		rb.linearVelocity = velocity;
	}

	#endregion
}