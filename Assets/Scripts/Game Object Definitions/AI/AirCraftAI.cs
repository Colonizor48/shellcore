﻿using System.Collections.Generic;
using UnityEngine;

public class AirCraftAI : MonoBehaviour
{
    public enum AIMode
    {
        Follow,
        Path,
        Battle,
        Inactive
    }

    enum AIState
    {
        Inactive,
        Active,
        Retreating
    }

    public enum AIAggression
    {
        FollowInRange,
        StopToAttack,
        KeepMoving
    }

    private AIMode mode;
    private AIState state;

    public AIAggression aggression;
    public Craft craft;
    public IOwner owner;
    private AIModule module;

    private Entity aggroTarget; // Overrides curren't module's movement if aggression level allowes that
    float aggroSearchTimer = 0f;

    public bool allowRetreat;
    float retreatSearchTimer = 0f;
    bool retreatTargetFound = false;
    Vector2 retreatTarget;

    //public static List<Entity> entities = new List<Entity>();

    public void setMode(AIMode mode)
    {
        if (mode == this.mode)
            return;

        this.mode = mode;

        switch (mode)
        {
            case AIMode.Follow:
                module = new FollowAI();
                break;
            case AIMode.Path:
                module = new PathAI();
                break;
            case AIMode.Battle:
                module = new BattleAI();
                break;
            case AIMode.Inactive:
                module = null;
                break;
            default:
                break;
        }
        if(module != null)
        {
            module.craft = craft;
            module.owner = owner;
            module.ai = this;
            module.Init();
        }

    }

    public void follow(Transform t = null)
    {
        setMode(AIMode.Follow);
        (module as FollowAI).followTarget = t;
        module.Init();
    }

    public AIMode getMode()
    {
        return mode;
    }

    public void moveToPosition(Vector2 pos)
    {
        setMode(AIMode.Path);
        (module as PathAI).MoveToPosition(pos);
    }

    public void setPath(Path path)
    {
        setMode(AIMode.Path);
        (module as PathAI).setPath(path);
        if(module != null) module.Init();
    }

    private void Start()
    {
        state = AIState.Active;
    }

    public void Init(Craft craft, IOwner owner = null)
    {
        this.owner = owner;
        this.craft = craft;
    }

    private void Update()
    {
        if (!craft.GetIsDead() && module != null)
        {
            foreach (Ability a in craft.GetAbilities())
            {
                if (a && a is WeaponAbility)
                {
                    (a as WeaponAbility).Tick("");
                }
            }

            if(aggression != AIAggression.KeepMoving && aggroSearchTimer < Time.time)
            {
                // find target, stop or follow, give up if it's outside range
                Entity target = getNearestEntity<Entity>(craft.transform.position, craft.faction, true, craft.Terrain);
                if (target && (target.transform.position - craft.transform.position).sqrMagnitude < 800f)
                {
                    aggroTarget = target;
                    //Debug.Log("AggroTarget: " + aggroTarget.name + " Factions: " + aggroTarget.faction + " - " + craft.faction);
                }

                aggroSearchTimer = Time.time + 1f;
            }

            if (state == AIState.Active)
            {
                module.StateTick();

                if (aggroTarget == null)
                {
                    module.ActionTick();

                    if (allowRetreat)
                    {
                        // check if retreat necessary
                        if (craft.GetHealth()[0] < 0.1f * craft.GetMaxHealth()[0])
                        {
                            state = AIState.Retreating;
                            Debug.LogFormat("Faction {0} retreating!", craft.faction);
                        }
                    }
                }
                else
                {
                    if(aggroTarget.GetIsDead())
                    {
                        aggroTarget = null;
                        aggroSearchTimer = 0f;
                    }
                    else
                    {
                        switch (aggression)
                        {
                            case AIAggression.FollowInRange:
                                // Follow
                                Vector3 delta = aggroTarget.transform.position - craft.transform.position;
                                float dist = delta.sqrMagnitude;
                                if(dist < 1000f)
                                {
                                    if (dist > 16f)
                                    {
                                        craft.MoveCraft(delta.normalized);
                                    }
                                }
                                else
                                {
                                    aggroTarget = null;
                                }
                                break;
                            case AIAggression.StopToAttack:
                                // Don't move
                                break;
                            case AIAggression.KeepMoving:
                                // Back to module's movement
                                aggroTarget = null;
                                break;
                            default:
                                break;
                        }

                    }
                }
            }
            else if (state == AIState.Retreating)
            {
                if ((!retreatTargetFound && retreatSearchTimer < Time.time) || retreatSearchTimer < Time.time)
                {
                    Entity enemy = getNearestEntity<Entity>(craft.transform.position, craft.faction, true, Entity.TerrainType.All);
                    if (enemy && (enemy.transform.position - craft.transform.position).sqrMagnitude < 1600f)
                    {
                        retreatTarget = (craft.transform.position - enemy.transform.position).normalized * 20f;
                        retreatTargetFound = true;
                        Debug.Log("retreat target found!");
                    }
                    else
                        retreatTargetFound = false;
                    retreatSearchTimer = Time.time + 1.0f;
                }
                else if(retreatTargetFound)
                {
                    Vector2 delta = retreatTarget - (Vector2)craft.transform.position;
                    if (delta.sqrMagnitude > 4f)
                    {
                        craft.MoveCraft(delta.normalized);
                    }
                    else
                    {
                        retreatSearchTimer = Time.time;
                    }
                }
                else
                {
                    Vector2 delta = craft.spawnPoint - craft.transform.position;
                    if (delta.sqrMagnitude > 4f)
                    {
                        craft.MoveCraft(delta.normalized);
                    }
                }

                // check if retreat necessary anymore
                if (craft.GetHealth()[0] > 0.1f * craft.GetMaxHealth()[0])
                {
                    state = AIState.Active;
                    Debug.LogFormat("Faction {0} stopped retreating!", craft.faction);
                }

            }
            else
            {
                if(state == AIState.Inactive)
                {
                    module.Init();
                    state = AIState.Active;
                }
            }
        }
        else
        {
            state = AIState.Inactive;
        }
    }

    public static T getNearestEntity<T>(Vector3 position, int faction = -1, bool enemy = true, Entity.TerrainType terrainType = Entity.TerrainType.All) where T : Entity
    {
        float minD = float.MaxValue;
        T nearest = null;
        for (int i = 0; i < AIData.entities.Count; i++)
        {
            if (AIData.entities[i].GetIsDead())
                continue;
            if (terrainType != Entity.TerrainType.All && AIData.entities[i].Terrain != terrainType)
                continue;
            if (AIData.entities[i] is T)
            {
                if (((AIData.entities[i].faction == faction) ^ !enemy) && faction != -1)
                    continue;

                float d = (position - AIData.entities[i].transform.position).sqrMagnitude;
                if (d < minD)
                {
                    minD = d;
                    nearest = AIData.entities[i] as T;
                }
            }
        }
        return nearest;
    }

    public static int getEnemyCountInRange(Vector3 position, float range, int faction)
    {
        int count = 0;
        float sqrRange = range * range;
        for (int i = 0; i < AIData.entities.Count; i++)
        {
            if (AIData.entities[i].GetIsDead())
                continue;
            if ((position - AIData.entities[i].transform.position).sqrMagnitude < sqrRange)
                count++;
        }
        return count;
    }
}
