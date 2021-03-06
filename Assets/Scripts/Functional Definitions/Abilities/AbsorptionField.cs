﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Immobilizes the craft and converts all damage to energy
/// </summary>
public class AbsorptionField : ActiveAbility
{
    Craft craft;
    GameObject field;

    protected override void Awake()
    {
        base.Awake(); // base awake
        ID = AbilityID.Absorb;
        cooldownDuration = 10;
        CDRemaining = cooldownDuration;
        activeDuration = 1;
        activeTimeRemaining = activeDuration;
        energyCost = 100;
    }

    private void Start()
    {
        craft = Core as Craft;
    }

    /// <summary>
    /// Removes the field sprite
    /// </summary>
    protected override void Deactivate()
    {
        ToggleIndicator(true);
        Destroy(field);
        craft.isAbsorbing = false;
        craft.isImmobile = false;
    }

    /// <summary>
    /// Creates the field sprite
    /// </summary>
    protected override void Execute()
    {
        if(craft)
        {
            craft.isAbsorbing = true;
            craft.isImmobile = true;
            field = new GameObject("Field");
            field.transform.SetParent(craft.transform);
            field.transform.localScale = Vector3.one;
            field.transform.localPosition = Vector3.zero;
            var sr = field.AddComponent<SpriteRenderer>();
            sr.sprite = ResourceManager.GetAsset<Sprite>("absorption_sprite");
            sr.sortingOrder = 1000;
        }
        AudioManager.PlayClipByID("clip_activateability", transform.position);
        // adjust fields
        isActive = true; // set to active
        isOnCD = true; // set to on cooldown
        ToggleIndicator(true);
    }
}
