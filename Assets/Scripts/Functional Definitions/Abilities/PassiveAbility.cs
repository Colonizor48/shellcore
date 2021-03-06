﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PassiveAbility : Ability {

    private bool initialized = false;

    protected override void Awake()
    {
        base.Awake();
        abilityName = "Passive Ability";
    }

    protected override void Execute()
    {
        
    }

    public override void Tick(int key)
    {
        if (!initialized)
        {
            Execute();
            initialized = true;
        }
    }
}
