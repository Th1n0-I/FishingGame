using UnityEngine;
using UnityEngine.InputSystem;

public class cameraMovement : MonoBehaviour {
	[Header("Settings")]
	[SerializeField] private float lookSens;
	[SerializeField] private float moveSens;
	[SerializeField] private float maxLookAngle;
	
	private Transform tr;

	private InputAction moveAction;
	private InputAction elevationAction;
	private InputAction lookAction;
	private InputAction pauseAction;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        tr = GetComponent<Transform>();
        
        moveAction = InputSystem.actions.FindAction("Move");
        elevationAction = InputSystem.actions.FindAction("Elevation");
        lookAction = InputSystem.actions.FindAction("Look");
        pauseAction = InputSystem.actions.FindAction("Pause");
    }

    // Update is called once per frame
    void Update() {
	    CheckPlayerInputs();
    }

    private void CheckPlayerInputs() {
	    if (pauseAction.triggered) Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
	    
	    Vector2 move = moveAction.ReadValue<Vector2>();
	    float elevate = elevationAction.ReadValue<float>();
	    tr.position += (tr.forward * move.y + tr.right * move.x + tr.up * elevate) * (Time.deltaTime * moveSens * (Cursor.lockState == CursorLockMode.Locked ? 1 : 0));
	    
	    Vector2 look = lookAction.ReadValue<Vector2>();
	    tr.eulerAngles += new Vector3(-look.y, look.x, 0.0f) * (lookSens * (Cursor.lockState == CursorLockMode.Locked ? 1 : 0));
	    
	    float pitch = tr.eulerAngles.x;
	    if (pitch > 180) pitch -= 360;
	    if (pitch > maxLookAngle) pitch = maxLookAngle;
	    if  (pitch < -maxLookAngle) pitch = -maxLookAngle;
	    tr.eulerAngles = new Vector3(pitch,tr.eulerAngles.y, tr.eulerAngles.z);
    }
}

