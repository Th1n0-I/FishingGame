using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour {
	#region fields
	
	//? Components
	private Rigidbody rb;
	
	#endregion
	
	#region Unity functions
	
    void Start()
    {
        rb  = GetComponent<Rigidbody>();
        Cursor.lockState = CursorLockMode.Locked;
    }

    // Update is called once per frame
    void Update() {
	    CheckPlayerInputs();
    }
    
    #endregion
    
    #region custom functions

    private void CheckPlayerInputs() {
	    var moveVector = InputSystem.actions.FindAction("Move").ReadValue<Vector2>();
	    rb.linearVelocity += new Vector3(moveVector.x, 0, moveVector.y);
	    
	    var lookVector = InputSystem.actions.FindAction("Look").ReadValue<Vector2>();
    }
    
    #endregion
}
