using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProjectileHitboxScript : MonoBehaviour
{
    public GameObject enemyObj;

    // Used to check if the collider other enters the trigger
    private void OnTriggerEnter(Collider other)
    {
        // If the projectile hits the enemy, it takes damage
        if (other.gameObject.CompareTag("Projectile"))
        {
            enemyObj.ApplyDamage(enemyObj, 1);
        }
    }
}
