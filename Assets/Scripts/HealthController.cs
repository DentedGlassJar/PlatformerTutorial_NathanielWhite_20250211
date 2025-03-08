using UnityEngine;
using System.Collections;
using UnityEngine.Events;
using System.Linq;
using Unity.VisualScripting;

/// <summary>
/// Handles health, damage, and death behavior for game objects.
/// Attach to any object that can take damage (players, enemies, destructible objects, etc.)
/// </summary>
public class HealthController : MonoBehaviour 
{
	[Header("Audio")]
	[Tooltip("Sound effect played when this object collides with something")]
	public AudioClip collisionAudioClip;
	[Tooltip("Sound effect played when this object takes damage")]
	public AudioClip damageAudioClip;
	[Tooltip("Sound effect played when this object dies")]
	public AudioClip deathAudioClip;

	[Header("Health Settings")]
	[Tooltip("Starting health amount of this object")]
	[SerializeField] private int health = 1;
	[Tooltip("Whether this object takes damage from physical collisions")]
	public bool takesCollisionDamageFromRigidbodies;
    [Tooltip("Whether this object takes damage from physical collisions")]
    public bool takesCollisionDamageFromStaticbodies;
    [Tooltip("Minimum speed a collision hit must have to deal damage if takesCollisionDamage is true.")]
    public float minCollisionDamageVelocity = 8.2f;
    [Tooltip("Whether this object should respawn after death. 0 Means no respawns, -1 means infinite respawns, any number above is treated as lives.")]
	public int shouldRespawn = 0;
    [Tooltip("Seconds to wait before respawn (if respawning)")]
    public float respawnAfterSeconds = 1.5f;
    [Tooltip("Object tags that won't cause collision damage to this object")]
	[TagDropdown]
	public string[] ignoredCollisionTags;

	[Header("Damage Visual Effects")]
	[Tooltip("Time between each damage flash (lower = faster flashing)")]
	public float damageFlashInterval = 0.1f;
	[Tooltip("Duration of invulnerability and flashing effect after taking damage")]
	public float invulnerabilityDuration = 0.9f;
	[Tooltip("Color to flash when taking damage")]
	public Color damageFlashColor = Color.red;
	[Tooltip("Renderers that will flash when taking damage. If empty, uses all color-capable renderers")]
	public Renderer[] renderersToFlash;

	[Header("Death Effects")]
	[Tooltip("Objects to spawn when this object dies (particles, pickups, etc.)")]
	public GameObject[] deathSpawnables;

	[Header("Events")]
    public UnityEvent<int,int> OnHealthChanged = new UnityEvent<int,int>();
    public UnityEvent OnDeath = new UnityEvent();

	// Hidden public state variables
	public bool isDead { get => health <= 0; }
	[HideInInspector] public Vector3 respawnPosition;
	
	private Color[] defaultColors;
	private int maxHealth, previousHealth, collisionDamage;
	private bool isFlashingDamageColor = false;
	private float nextFlashTime, damageFlashEndTime, spawnedInTime;
	
	// Separate invulnerability from damage flash
	public bool isInvulnerable { get; private set; }
	private bool isDamageFlashing;

	private Animator animator;
	
	/// <summary>
	/// Initializes health component, validates settings, and finds required components
	/// </summary>
	void Awake()
	{
        spawnedInTime = Time.time;
        previousHealth = health;
		maxHealth = health;
		respawnPosition = transform.position;
		animator = GetComponentInChildren<Animator>();
		InitializeFlashRenderers();
	}
	
	/// <summary>
	/// Handles damage detection, visual effects, and death state
	/// </summary>
	void Update()
	{		
		HandleDamageFlashing();
	}
	
	/// <summary>
	/// Sets up renderers for damage flash effect
	/// </summary>
	private void InitializeFlashRenderers()
	{
		if(renderersToFlash == null || renderersToFlash.Length == 0) {
			renderersToFlash = GetComponentsInChildren<Renderer>()
				.Where((renderer) => renderer.material.HasProperty("_Color"))
				.ToArray();
		}
		
		if (renderersToFlash.Length > 0) {
			defaultColors = new Color[renderersToFlash.Length];
			for (int i = renderersToFlash.Length - 1; i >= 0 ; i--)
				defaultColors[i] = renderersToFlash[i].material.color;
		}
	}
	
	/// <summary>
	/// Call to apply damage to an object and trigger damage effects
	/// </summary>
	public void ApplyDamage(int damage)
	{
		health = Mathf.Max(health - damage, 0);
		if (health < previousHealth)
		{
			OnHealthChanged?.Invoke(previousHealth, health);
			StartDamageFlash();
			
			if (damageAudioClip)
				AudioSource.PlayClipAtPoint(damageAudioClip, transform.position);
			if (isDead) {
				HandleDeath();
			}
			else
			{
				if (animator)
					animator.SetTrigger(MovementController.AnimationID_Hurt);
			}
        }
		previousHealth = health;
	}
	
	/// <summary>
	/// Starts the damage flash effect and temporary invulnerability
	/// </summary>
	private void StartDamageFlash()
	{
		isDamageFlashing = true;
		damageFlashEndTime = Time.time + invulnerabilityDuration;
	}
	
	/// <summary>
	/// Manages the damage flash visual effect
	/// </summary>
	private void HandleDamageFlashing()
	{
		if (isDamageFlashing)
		{
			FlashDamageEffect();
			if (Time.time > damageFlashEndTime)
			{
				UpdateRendererColors();
				isDamageFlashing = false;
			}
		}
	}
	
	/// <summary>
	/// Toggles the damage flash effect on renderers
	/// </summary>
	private void FlashDamageEffect()
	{
		UpdateRendererColors(isFlashingDamageColor);
		
		if(Time.time > nextFlashTime)
		{
			isFlashingDamageColor = !isFlashingDamageColor;
			nextFlashTime = Time.time + damageFlashInterval;
		}
	}
	
	
	/// <summary>
	/// Handles the death sequence including effects, respawn, or destruction
	/// </summary>
	private void HandleDeath()
	{
		OnDeath?.Invoke();
		
		if (deathAudioClip)
			AudioSource.PlayClipAtPoint(deathAudioClip, transform.position);
		
		isDamageFlashing = false;
		UpdateRendererColors();

        if (animator)
            animator.SetBool(MovementController.AnimationID_Death, true);
        
		if (shouldRespawn != 0) {
			StartCoroutine(RespawnEntity());
		}
		else
		{
			SpawnDeathObjects();
			Destroy(gameObject);
		}
	}
	
	/// <summary>
	/// Respawns the entity at the appropriate position and resets its state
	/// </summary>
	private IEnumerator RespawnEntity()
	{
		yield return new WaitForSeconds(respawnAfterSeconds);
		Vector3 respawnPoint = respawnPosition;
		
		// If this is a player and there's a valid checkpoint, use that instead
		if (CompareTag("Player")) {
			CheckpointManager.TeleportPlayerToCheckpoint(gameObject);
			respawnPoint = transform.position;
		}

		if (TryGetComponent<Rigidbody>(out Rigidbody rigidbody))
		{
			rigidbody.velocity = Vector3.zero;
			rigidbody.position = respawnPoint;
		}
			
		transform.position = respawnPoint;
		health = maxHealth;
		spawnedInTime = Time.time;

        if (animator)
			animator.SetBool(MovementController.AnimationID_Death, false);
	}
	
	private void SpawnDeathObjects()
	{
		if (deathSpawnables.Length == 0) return;
		
		foreach(GameObject prefab in deathSpawnables)
			Instantiate(prefab, transform.position, Quaternion.identity);
	}

	/// <summary>
	/// Updates the color of all registered renderers
	/// </summary>
	/// <param name="useFlashColor">If true, uses the damage flash color; if false, uses original colors</param>
	private void UpdateRendererColors(bool useFlashColor = false) 
	{
		for (int i = renderersToFlash.Length - 1; i >= 0 ; i--) {
			renderersToFlash[i].material.color = useFlashColor ? damageFlashColor : defaultColors[i];
		}
	}
	
	/// <summary>
	/// Handles collision events and calculates potential damage
	/// </summary>
	/// <param name="collision">Information about the collision that occurred</param>
	void OnCollisionEnter(Collision collision)
	{
		if (!ShouldProcessCollisionDamage(collision)) return;
		
		CalculateAndApplyCollisionDamage(collision);
	}
	
	/// <summary>
	/// Determines if a collision should cause damage to this object
	/// </summary>
	/// <param name="collision">The collision to evaluate</param>
	/// <returns>True if the collision should cause damage, false otherwise</returns>
	private bool ShouldProcessCollisionDamage(Collision collision)
	{
		if (!takesCollisionDamageFromStaticbodies && !takesCollisionDamageFromRigidbodies) return false;
		
		foreach(string tag in ignoredCollisionTags)            
			if(collision.transform.CompareTag(tag))
				return false;

		if (takesCollisionDamageFromStaticbodies && !collision.rigidbody) {
			if (collision.relativeVelocity.magnitude < minCollisionDamageVelocity)
				return false;
		} else if (takesCollisionDamageFromRigidbodies && collision.rigidbody)
		{
            if (Mathf.Max(collision.rigidbody.velocity.magnitude, collision.relativeVelocity.magnitude) < minCollisionDamageVelocity)
                return false;
        }
		return true;
	}
	
	/// <summary>
	/// Calculates and applies damage based on collision properties
	/// </summary>
	/// <param name="collision">The collision used to calculate damage</param>
	private void CalculateAndApplyCollisionDamage(Collision collision)
	{
		if (!CanTakeDamage()) return;

		collisionDamage = 0;
		if (collision.rigidbody && takesCollisionDamageFromRigidbodies)
		{
			collisionDamage = (int)Mathf.RoundToInt((collision.rigidbody.velocity.magnitude * 0.2f) * collision.rigidbody.mass);
		}
		else if (!collision.rigidbody && takesCollisionDamageFromStaticbodies)
		{
            collisionDamage = (int)Mathf.RoundToInt((collision.relativeVelocity.magnitude) * 0.1f);
		}
		if (collisionDamage > 0)
			ApplyDamage(collisionDamage);
	}

	/// <summary>
	/// Sets the invulnerability state of this object without visual effects
	/// </summary>
	/// <param name="invulnerable">Whether the object should be invulnerable to damage</param>
	public void SetInvulnerable(bool invulnerable)
	{
		isInvulnerable = invulnerable;
	}

	/// <summary>
	/// Checks if the object can take damage
	/// </summary>
	private bool CanTakeDamage()
	{
		return !isInvulnerable && damageFlashEndTime < Time.time && spawnedInTime.HasTimeElapsedSince(0.33f);
	}
}
