using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles player input, animation, and feedback effects for character movement.
/// Works in conjunction with AdvancedMoveController to provide a complete character control system.
/// </summary>
[RequireComponent(typeof(AdvancedMoveController))]
public class PlayerController : MonoBehaviour
{
    public ThirdPersonCamera CameraFollower {get; private set;}
    private Animator characterAnimator;
    private AdvancedMoveController moveController;
    private Rigidbody rb;
    private DashController dashController;
    
    // Movement state
    private Vector3 moveDirection;
    private Vector3 cameraAlignedForward;
    private Vector3 cameraAlignedRight;
    private Vector3 inputVector;

    private HealthController healthComponent;
    private PlayerInput playerInput;

    // A variable used to get the reference to the projectile GameObject    
    public GameObject projectileObj;

    // A variable used to get the reference of the projectile's rigidbody
    private Rigidbody pelletRb;

    // A bool that checks to see if the player has shot a pellet, and that it hasn't disappeared yet
    private bool hasPelletBeenUsed;

    // A vector3 that gets the forward of the player gameObject
    private Vector3 fwd;

    // A variable used to check the distance between the player and the pellet
    private Vector3 direction;

    // A float used for setting the maximum length the direction variable can go to
    private float maxLength = 10f;

    // A float used for multiplying with the forward vector of the player gameObject
    private float pelletPower = 5f;
    
    public bool JoinedThroughGameManager { get; set; } = false;
    public static List<PlayerController> players = new List<PlayerController>();
    private void OnEnable()
    {
        if(moveController != null)
            moveController.enabled = true;
    }

    private void OnDisable()
    {
        if (moveController != null)
            {
                inputVector = Vector3.zero;
                moveDirection = Vector3.zero;
                rb.velocity = Vector3.zero;
                moveController.ApplyMovement(Vector3.zero);
                moveController.UpdateMovement();
                moveController.enabled=false;
                UpdateVisualFeedback();
            }
    }

    /// <summary>
    /// Initialize components and verify required setup
    /// </summary>
    void Awake()
    {
        players.Add(this);
        // Ensure correct tag for player identification
        if(!gameObject.CompareTag("Player"))
            tag = "Player";

        TryGetComponent(out playerInput);
        TryGetComponent(out dashController);

        // Cache component references
        moveController = GetComponent<AdvancedMoveController>();
        rb = GetComponent<Rigidbody>();
        CameraFollower = GetComponentInChildren<ThirdPersonCamera>();
        characterAnimator = GetComponentInChildren<Animator>();
        healthComponent = GetComponent<HealthController>();

        if (CameraFollower)
        {
            if (playerInput.camera == null) {
                //Debug.Log(actions["Jump"].GetBindingDisplayString());
                playerInput.camera = CameraFollower.GetComponent<Camera>();
            }
            CameraFollower.transform.SetParent(transform.parent);
            DontDestroyOnLoad(CameraFollower.gameObject);
        }

        DontDestroyOnLoad(gameObject);
    }

    public void Start()
    {
        if (!JoinedThroughGameManager)
        {
            Destroy(gameObject);
            return;
        }
        CheckpointManager.TeleportPlayerToCheckpoint(gameObject);

        // Makes the hasPelletBeenUsed variable false
        hasPelletBeenUsed = false;
    }

    /// <summary>
    /// Clean up camera follower on destruction
    /// </summary>
    void OnDestroy()
    {
        if (players.Contains(this))
            players.Remove(this);
        if (playerInput)
            Destroy(playerInput);
        if (CameraFollower)
            Destroy(CameraFollower.gameObject);
    }

    void OnMove(InputValue inputVal)
    {
        if (GameManager.Instance.IsShowingPauseMenu)
            inputVector = Vector3.zero;
        else
            inputVector = inputVal.Get<Vector2>();
    }
    /// <summary>
    /// Handle jump input from the input system
    /// </summary>
    void OnJump()
    {
        if (!GameManager.Instance.IsShowingPauseMenu)
            moveController.RequestJump();
    }

    void OnPause()
    {
        GameManager.Instance.TogglePauseMenu();
    }

    /// <summary>
    /// Handle dash input from the input system
    /// </summary>
    void OnDash()
    {
        if (!GameManager.Instance.IsShowingPauseMenu && dashController)
            dashController.TryStartDash(moveDirection);
    }

    void OnCameraOrbit(InputValue inputVal)
    {
        CameraFollower.OrbitInput = inputVal.Get<float>();
    }

    // --NATHANIEL'S--
    // This methods does two things, it makes the forward vector of where the pellets will go equal to the camera's forward vector, and
    // it'll check and see if the player pressed the left mouse button.
    public void OnShoot()
    {
        // Checks and makes the fwd variable equal the player's forward vector
        fwd = gameObject.transform.forward;

        // Gets the distance between the player and the projectile
        direction = gameObject.transform.position - projectileObj.transform.position;

        if (hasPelletBeenUsed == false)
        {
            hasPelletBeenUsed = true;

            // Makes a clone of the projectile Prefab, that spawns from the player's position and has a rotation of zero
            projectileObj = Instantiate(projectileObj, gameObject.transform.position, gameObject.transform.rotation);

            // Gets the rigidbody of the projectileObj
            pelletRb = projectileObj.GetComponent<Rigidbody>();

            // Checks to see if the distance between the player and pellet goes over the maxLength float, if it does it destroys the pellet
            // and makes it so you can shoot again
            if (direction.magnitude >= maxLength)
            {
                Debug.Log($"Pellet is destroyed!");
            }
        }
    }

    /// <summary>
    /// Calculate movement direction based on camera orientation
    /// </summary>
    void Update()
    {
        // Convert input to camera-relative movement direction
        Quaternion cameraRotation = Quaternion.Euler(0, CameraFollower.transform.eulerAngles.y, 0);
        cameraAlignedForward = cameraRotation * Vector3.forward;
        cameraAlignedRight = cameraRotation * Vector3.right;
        
        moveDirection = ((cameraAlignedForward * inputVector.y) + (cameraAlignedRight * inputVector.x)).normalized;

        // Makes the pellet gameObject go in the direction of the player's forward vector, times the float variable pelletPower
        pelletRb.AddForce(fwd * pelletPower);
    }

    /// <summary>
    /// Handle physics-based movement and animation updates
    /// </summary>
    void FixedUpdate()
    {
        if (moveController.enabled) {
            moveController.ApplyMovement(moveDirection);
            moveController.UpdateMovement();
        }

        // Normal movement
        UpdateVisualFeedback();

        if (transform.position.y < -50f) {
            CheckpointManager.TeleportPlayerToCheckpoint(gameObject);
            if (CameraFollower)
                CameraFollower.transform.position = gameObject.transform.position;
        }
    }

    /// <summary>
    /// Update animator parameters and handle squash/stretch effects
    /// </summary>
    private void UpdateVisualFeedback()
    {
        if (!characterAnimator) return;

        // Update animator parameters
        characterAnimator.SetFloat(MovementController.AnimationID_DistanceToTarget, moveController.distanceToDestination);
        characterAnimator.SetBool(MovementController.AnimationID_IsGrounded, moveController.isGrounded);
        characterAnimator.SetFloat(MovementController.AnimationID_YVelocity, rb.velocity.y);
    }
} 