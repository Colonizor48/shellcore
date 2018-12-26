﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bullet : WeaponAbility {

    public GameObject bulletPrefab; // the prefabbed sprite for a bullet with a BulletScript
    protected float bulletSpeed; // the speed of the bullet
    protected float survivalTime; // the time the bullet takes to delete itself
    protected float damage;
    protected Vector3 prefabScale; // the scale of the bullet prefab, used to enlarge the siege turret bullet
    protected float pierceFactor = 0;


    protected override void Awake()
    {
        base.Awake(); // base awake
        // hardcoded values here
        bulletSpeed = 50;
        survivalTime = 0.5F;
        range = bulletSpeed * survivalTime;
        ID = 5;
        cooldownDuration = 0.4F;
        CDRemaining = cooldownDuration;
        energyCost = 10;
        damage = 100;
        prefabScale = 1 * Vector3.one;
        category = Entity.EntityCategory.All;
    }

    /// <summary>
    /// Fires the bullet using the helper method
    /// </summary>
    /// <param name="victimPos">The position to fire the bullet to</param>
    protected override bool Execute(Vector3 victimPos)
    {
        if (targetingSystem.GetTarget()) // check if there is actually a target, do not fire if there is not
        {
            FireBullet(victimPos); // fire if there is
            isOnCD = true; // set on cooldown
            return true;
        }
        return false;
    }

    /// <summary>
    /// Helper method for Execute() that creates a bullet and modifies it to be shot
    /// </summary>
    /// <param name="targetPos">The position to fire the bullet to</param>
    void FireBullet(Vector3 targetPos)
    {
        Vector3 originPos = part ? part.transform.position : Core.transform.position;
        // Create the Bullet from the Bullet Prefab
        Vector3 diff = targetPos - originPos;
        var bullet = Instantiate(bulletPrefab, originPos, Quaternion.Euler(new Vector3(0, 0, Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg - 90)));
        bullet.transform.localScale = prefabScale;

        // Update its damage to match main bullet
        var script = bullet.GetComponent<BulletScript>();
        script.SetDamage(damage);
        script.SetCategory(category);
        script.SetTerrain(terrain);
        script.SetShooterFaction(Core.faction);
        script.SetPierceFactor(pierceFactor);

        // Add velocity to the bullet
        bullet.GetComponent<Rigidbody2D>().velocity = Vector3.Normalize(targetPos - originPos) * bulletSpeed;

        // Destroy the bullet after survival time
        Destroy(bullet, survivalTime);
    }
}
