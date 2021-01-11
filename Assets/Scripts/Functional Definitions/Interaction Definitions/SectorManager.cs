﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEditor;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(LandPlatformGenerator))]
public class SectorManager : MonoBehaviour
{
    public delegate void SectorLoadDelegate(string sectorName);
    public static SectorLoadDelegate OnSectorLoad;
    public static SectorManager instance;
    public static string customPath = "";
    public bool jsonMode;
    public List<Sector> sectors;
    public PlayerCore player;
    public Sector current;
    public BackgroundScript background;
    public InfoText info;
    [HideInInspector]
    public string resourcePath = "";
    private Dictionary<int, int> stationsCount = new Dictionary<int, int>();
    public Dictionary<int, ICarrier> carriers = new Dictionary<int, ICarrier>();
    private List<IVendor> stations = new List<IVendor>();
    private BattleZoneManager battleZone;
    private SiegeZoneManager siegeZone;
    private Dictionary<string, GameObject> objects;
    private Dictionary<string, GameObject> persistentObjects;
    private LandPlatformGenerator lpg;
    private LineRenderer sectorBorders;
    private Dictionary<Sector, LineRenderer> minimapSectorBorders;
    private int uniqueIDInt;
    private bool sectorLoaded = false;
    public Vector2 spawnPoint;
    public WorldData.CharacterData[] characters; // Unity initializes public arrays, remember!
    public DialogueSystem dialogueSystem;
    public static Sector GetSectorByName(string sectorName) 
    {
        foreach(var sector in instance.sectors)
        {
            if(sector.sectorName == sectorName) return sector;
        }

        // TODO: handle a null return (not supposed to ever happen)
        return null;
    }

    public int GetExtraCommandUnits(int faction) {
        stationsCount.Clear();
        foreach(IVendor vendor in stations)
        {
            int stationFaction = (vendor as Entity).faction;
            if(!stationsCount.ContainsKey(stationFaction))
            {
                stationsCount.Add(stationFaction, 0);
            }
            stationsCount[stationFaction]++;
        }
        return stationsCount.ContainsKey(faction) ? stationsCount[faction] * 3 : 0; 
    }

    public static string testJsonPath = null;
    public static string testResourcePath = null;
    public static string jsonPath = Application.streamingAssetsPath + "\\Sectors\\main - " + VersionNumberScript.mapVersion;
    public void Initialize()
    {
        if (instance != null)
        {
            Debug.LogWarning("There should be only one sector manager!");
            Destroy(gameObject);
            return;
        }
        instance = this;

        objects = new Dictionary<string, GameObject>();
        persistentObjects = new Dictionary<string, GameObject>();
        battleZone = gameObject.AddComponent<BattleZoneManager>();
        siegeZone = gameObject.AddComponent<SiegeZoneManager>();
        battleZone.enabled = false;
        siegeZone.enabled = false;
        lpg = GetComponent<LandPlatformGenerator>();
        lpg.Initialize();
        sectorBorders = new GameObject("SectorBorders").AddComponent<LineRenderer>();
        sectorBorders.enabled = false;
        sectorBorders.positionCount = 4;
        sectorBorders.startWidth = 0.15f;
        sectorBorders.endWidth = 0.15f;
        sectorBorders.loop = true;
        OnSectorLoad = null;

        if (customPath != "" && current == null)
        {
            jsonPath = customPath;
            jsonMode = true;
        }


            
		
		if(SceneManager.GetActiveScene().name == "MainMenu")
		{
            string currentPath;
            if(!File.Exists(Application.persistentDataPath + "\\CurrentSavePath"))
			    currentPath = null;
            else currentPath = File.ReadAllLines(Application.persistentDataPath + "\\CurrentSavePath")[0];

			if(File.Exists(currentPath))
			{
				string json = File.ReadAllText(currentPath);
				var save = JsonUtility.FromJson<PlayerSave>(json);
				SetMainMenuSector(save.episode);
			}
			else 
            {
                SetMainMenuSector(0);
            }
        }

        jsonMode = false;
    }

    private float dangerZoneTimer;
    public GameObject damagePrefab;
    private void Update()
    {
        if(jsonMode) player.SetIsInteracting(true);
        if(!jsonMode && player && (current == null || (!current.bounds.contains(player.transform.position) && !player.GetIsOscillating())))
        {
            AttemptSectorLoad();
        }

        // deadzone damage
        if(current && current.type == Sector.SectorType.DangerZone)
        {
            if(dangerZoneTimer >= 5 && !player.GetIsDead())
            {
                dangerZoneTimer = 0;
                Instantiate(damagePrefab, player.transform.position, Quaternion.identity);
                player.TakeShellDamage(0.2F * player.GetMaxHealth()[0], 0, null);
                player.TakeCoreDamage(0.2F * player.GetMaxHealth()[1]);
                player.alerter.showMessage("WARNING: Leave Sector!", "clip_stationlost");
            } else dangerZoneTimer += Time.deltaTime;
        } else
        {
            dangerZoneTimer = 0;
        }
        
        if(!DialogueSystem.isInCutscene)
        {
            bgSpawnTimer += Time.deltaTime;

            if(bgSpawnTimer >= 8 && bgSpawns.Count > 0)
            {
                bgSpawnTimer = 0;
                var key = bgSpawns[Random.Range(0, bgSpawns.Count)];
                var spawnPoint = player.transform.position + Quaternion.Euler(0, 0, Random.Range(0, 360)) * new Vector3(key.Item4, 0, 0);
                key.Item2.position = spawnPoint;
                key.Item2.ID = "";
                SpawnEntity(key.Item1, key.Item2);
                AudioManager.PlayClipByID("clip_respawn", spawnPoint);
            }
        }
    }

    public void AttemptSectorLoad()
    {
        if(player && (current == null || (!current.bounds.contains(player.transform.position))))
        {
            // load sector
            for(int i = 0; i < sectors.Count; i++)
            {
                if(sectors[i].bounds.contains(player.transform.position))
                {
                    current = sectors[i];
                    loadSector();
                    break;
                }
            }
        }
    }

    public void TryGettingJSON() {
        string path = GameObject.Find("Path Input").GetComponent<UnityEngine.UI.InputField>().text;
        GameObject.Find("Path Input").transform.parent.gameObject.SetActive(false);
        LoadSectorFile(path);
    }

    public TaskManager taskManager;
    public void LoadSectorFile(string path)
    {
        // Update passed path during load time for main saves updating to newer versions
        if(path.Contains("main")) path = jsonPath;

        resourcePath = path;
        if (System.IO.Directory.Exists(path))
        {
            try
            {
                // resource pack loading
                if (!ResourceManager.Instance.LoadResources(path) && testResourcePath != null)
                {
                    ResourceManager.Instance.LoadResources(testResourcePath);
                }

                // canvas handling
                taskManager.ClearCanvases();
                dialogueSystem.ClearCanvases();

                foreach (var canvas in Directory.GetFiles(path + "\\Canvases"))
                {
                    if(canvas.Contains(".meta")) continue;
                    
                    if(canvas.Contains(".taskdata"))
                    {
                        taskManager.AddCanvasPath(canvas);
                        continue;
                    }

                    if(canvas.Contains(".dialoguedata"))
                    {
                        dialogueSystem.AddCanvasPath(canvas);
                        continue;
                    }
                }


                // sector and world handling
                string[] files = Directory.GetFiles(path);
                current = null;
                sectors = new List<Sector>();
                minimapSectorBorders = new Dictionary<Sector, LineRenderer>();
                foreach (string file in files)
                {
                    if(file.Contains(".meta") || file.Contains("ResourceData.txt")) continue;

                    // parse world data
                    if(file.Contains(".worlddata"))
                    {
                        string worlddatajson = System.IO.File.ReadAllText(file);
                        WorldData wdata = ScriptableObject.CreateInstance<WorldData>();
                        JsonUtility.FromJsonOverwrite(worlddatajson, wdata);
                        spawnPoint = wdata.initialSpawn;
                        if(player.cursave == null || player.cursave.timePlayed == 0)
                        {
                            player.transform.position = player.spawnPoint = spawnPoint;
                            if(wdata.defaultBlueprintJSON != null && wdata.defaultBlueprintJSON != "")
                            {
                                if(player.cursave != null)
                                    player.cursave.currentPlayerBlueprint = wdata.defaultBlueprintJSON;
                                Debug.Log("Default blueprint set");
                            }
                        }
                            
                        if(characters == null || characters.Length == 0) characters = wdata.defaultCharacters;
                        else
                        {
                            // if there were added characters into the map since the last save, they must be added
                            // into the existing data
                            List<WorldData.CharacterData> charList = new List<WorldData.CharacterData>(characters);
                            foreach(var defaultChar in wdata.defaultCharacters)
                            {
                                Debug.Log(defaultChar.ID);
                                if(!charList.TrueForAll(ch => ch.ID != defaultChar.ID))
                                {
                                    charList.Add(defaultChar);
                                }
                            }
                            characters = charList.ToArray();
                        }

                        PartIndexScript.index = wdata.partIndexDataArray;
                        continue;
                    }

                    string sectorjson = System.IO.File.ReadAllText(file);
                    SectorCreatorMouse.SectorData data = JsonUtility.FromJson<SectorCreatorMouse.SectorData>(sectorjson);
                    // Debug.Log("Platform JSON: " + data.platformjson);
                    // Debug.Log("Sector JSON: " + data.sectorjson);
                    Sector curSect = ScriptableObject.CreateInstance<Sector>();
                    JsonUtility.FromJsonOverwrite(data.sectorjson, curSect);

                    if (data.platformjson != "") // If the file has old platform data
                    {
                        LandPlatform plat = ScriptableObject.CreateInstance<LandPlatform>();
                        JsonUtility.FromJsonOverwrite(data.platformjson, plat);
                        plat.name = curSect.name + "Platform";
                        curSect.platform = plat;
                    }

                    // render the borders on the minimap
                    var border = new GameObject("MinimapSectorBorder - " + curSect.sectorName).AddComponent<LineRenderer>();
                    border.material = ResourceManager.GetAsset<Material>("white_material");
                    border.gameObject.layer = 8;
                    border.positionCount = 4;
                    border.startWidth = 0.5f;
                    border.endWidth = 0.5f;
                    border.loop = true;
                    border.SetPositions(new Vector3[]{
                        new Vector3(curSect.bounds.x, curSect.bounds.y, 0),
                        new Vector3(curSect.bounds.x + curSect.bounds.w, curSect.bounds.y, 0),
                        new Vector3(curSect.bounds.x + curSect.bounds.w, curSect.bounds.y - curSect.bounds.h, 0),
                        new Vector3(curSect.bounds.x, curSect.bounds.y - curSect.bounds.h, 0)
                    });
                    border.enabled = player.cursave.sectorsSeen.Contains(curSect.sectorName);
                    border.startColor = border.endColor = new Color32((byte)135, (byte)135, (byte)135, (byte)255);
                    minimapSectorBorders.Add(curSect, border);

                    sectors.Add(curSect);
                }
                player.SetIsInteracting(false);
                Debug.Log("worked");
                jsonMode = false;
                sectorLoaded = true;
                return;
            }
            catch (System.Exception e)
            {
                Debug.LogError(e);
            };
        }
        else if (System.IO.File.Exists(path))
        {
            try
            {
                string sectorjson = System.IO.File.ReadAllText(path);
                SectorCreatorMouse.SectorData data = JsonUtility.FromJson<SectorCreatorMouse.SectorData>(sectorjson);
                Debug.Log("Platform JSON: " + data.platformjson);
                Debug.Log("Sector JSON: " + data.sectorjson);
                Sector curSect = ScriptableObject.CreateInstance<Sector>();
                JsonUtility.FromJsonOverwrite(data.sectorjson, curSect);
                if (data.platformjson != "") // If the file has old platform data
                {
                    LandPlatform plat = ScriptableObject.CreateInstance<LandPlatform>();
                    JsonUtility.FromJsonOverwrite(data.platformjson, plat);
                    plat.name = curSect.name + "Platform";
                    curSect.platform = plat;
                }
                current = curSect;
                sectors = new List<Sector>();
                sectors.Add(curSect);
                Debug.Log("Success! File loaded from " + path);
                jsonMode = false;
                sectorLoaded = true;
                player.SetIsInteracting(false);
                loadSector();
                return;
            }
            catch (System.Exception e)
            {
                Debug.LogError(e);
            }
        }
        Debug.LogError("Could not find valid sector in " + path);
        jsonMode = false;
        player.SetIsInteracting(false);
        loadSector();
        sectorLoaded = true;
    }

    private void Start()
    {
        if(ResourceManager.Instance)sectorBorders.material = ResourceManager.GetAsset<Material>("white_material");

        if(!sectorLoaded)
        {
            // Main menu loader; only the main menu is not loaded by JSON anymore.
            // Look at how far we've come :)
            if(!jsonMode && current != null) 
            {
                loadSector();
                sectorLoaded = true;
            }
            else background.setColor(SectorColors.colors[5]);
        }
    }

    ///
    /// Sets the main menu sector based on the passed episode int.
    /// Assumes you are in the main menu scene.
    ///
    public void SetMainMenuSector(int episode)
    {
        if(!sectorLoaded)
        {
            if(episode >= sectors.Count) episode = sectors.Count-1;
            current = sectors[episode];
            VersionNumberScript.SetEpisodeName(episode);
        }
    }

    public Entity SpawnEntity(EntityBlueprint blueprint, Sector.LevelEntity data)
    {
        GameObject gObj = new GameObject(data.name);
        switch (blueprint.intendedType)
        {
            case EntityBlueprint.IntendedType.ShellCore:
                {
                    ShellCore shellcore = gObj.AddComponent<ShellCore>();
                    try
                    {
                        // Check if data has blueprint JSON, if it does override the current blueprint
                        // this now specifies the path to the JSON file instead of being the JSON itself
                        string json = data.blueprintJSON;
                        if (json != null && json != "")
                        {
                            blueprint = ScriptableObject.CreateInstance<EntityBlueprint>();

                            // try parsing directly, if that fails try fetching the entity file
                            try
                            {
                                JsonUtility.FromJsonOverwrite(json, blueprint);
                            }
                            catch
                            {
                                JsonUtility.FromJsonOverwrite(System.IO.File.ReadAllText
                                    (resourcePath + "\\Entities\\" + json + ".json"), blueprint);
                            }
                            
                            //Debug.Log(data.name);
                            blueprint.entityName = data.name;

                        } else shellcore.entityName = blueprint.entityName = data.name;

                        if(current.type == Sector.SectorType.BattleZone)
                        {
                            // set the carrier of the shellcore to the associated faction's carrier
                            if(carriers.ContainsKey(data.faction))
                                shellcore.SetCarrier(carriers[data.faction]);

                            battleZone.AddTarget(shellcore);
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.Log(e.Message);
                        //blueprint = obj as EntityBlueprint;
                    }
                    shellcore.sectorMngr = this;
                    break;
                }
            case EntityBlueprint.IntendedType.PlayerCore:
                {
                    if (player == null)
                    {
                        player = gObj.AddComponent<PlayerCore>();
                        player.sectorMngr = this;
                    }
                    else
                    {
                        Destroy(gObj);
                        return null;
                    }

                    break;
                }
            case EntityBlueprint.IntendedType.Turret:
                {
                    gObj.AddComponent<Turret>();
                    break;
                }
            case EntityBlueprint.IntendedType.Tank:
                {
                    gObj.AddComponent<Tank>();
                    break;
                }
            case EntityBlueprint.IntendedType.Bunker:
                {
                    Bunker bunker = gObj.AddComponent<Bunker>();
                    stations.Add(bunker);
                    bunker.vendingBlueprint =
                        blueprint.dialogue != null
                        ? blueprint.dialogue.vendingBlueprint
                        : ResourceManager.GetAsset<VendingBlueprint>(data.vendingID);
                    break;
                }
            case EntityBlueprint.IntendedType.Outpost:
                {
                    Outpost outpost = gObj.AddComponent<Outpost>();
                    stations.Add(outpost);
                    outpost.vendingBlueprint = 
                        blueprint.dialogue != null 
                        ? blueprint.dialogue.vendingBlueprint 
                        : ResourceManager.GetAsset<VendingBlueprint>(data.vendingID);
                    break;
                }
            case EntityBlueprint.IntendedType.Tower:
                {
                    break;
                }
            case EntityBlueprint.IntendedType.Drone:
                {
                    Drone drone = gObj.AddComponent<Drone>();
                    //drone.path = ResourceManager.GetAsset<Path>(data.pathID);
                    break;
                }
            case EntityBlueprint.IntendedType.AirCarrier:
                AirCarrier carrier = gObj.AddComponent<AirCarrier>();
                if (!carriers.ContainsKey(data.faction))
                {
                    carriers.Add(data.faction, carrier);
                }
                carrier.sectorMngr = this;
                break;
            case EntityBlueprint.IntendedType.GroundCarrier:
                GroundCarrier gcarrier = gObj.AddComponent<GroundCarrier>();
                if (!carriers.ContainsKey(data.faction))
                {
                    carriers.Add(data.faction, gcarrier);
                }
                gcarrier.sectorMngr = this;
                break;
            case EntityBlueprint.IntendedType.Yard:
                Yard yard = gObj.AddComponent<Yard>();
                yard.mode = BuilderMode.Yard;
                break;
            case EntityBlueprint.IntendedType.WeaponStation:
                gObj.AddComponent<WeaponStation>();
                break;
            case EntityBlueprint.IntendedType.CoreUpgrader:
                gObj.AddComponent<CoreUpgrader>();
                break;
            case EntityBlueprint.IntendedType.Trader:
                Yard trade = gObj.AddComponent<Yard>();
                trade.mode = BuilderMode.Trader;
                try
                {
                    bool ok = true;
                    if (blueprint.dialogue == null)
                    {
                        ok = false;
                        Debug.Log("No dialogue!");
                    }
                    if (blueprint.dialogue.traderInventory == null)
                    {
                        ok = false;
                        Debug.Log("No inventory!");
                    }
                    if (data.blueprintJSON == null || data.blueprintJSON == "")
                    {
                        ok = false;
                        Debug.Log("No blueprint!");
                    }
                    if (ok)
                    {
                        ShipBuilder.TraderInventory inventory = JsonUtility.FromJson<ShipBuilder.TraderInventory>(data.blueprintJSON);
                        if (inventory.parts != null)
                        blueprint.dialogue.traderInventory = inventory.parts;
                    }
                    else
                    {
                        blueprint.dialogue.traderInventory = new List<EntityBlueprint.PartInfo>();
                    }
                }
                catch(System.Exception e)
                {
                    Debug.LogWarning(e);
                    blueprint.dialogue.traderInventory = new List<EntityBlueprint.PartInfo>();
                }
                break;
            case EntityBlueprint.IntendedType.DroneWorkshop:
                Yard workshop = gObj.AddComponent<Yard>();
                workshop.mode = BuilderMode.Workshop;
                break;
            default:
                break;
        }
        Entity entity = gObj.GetComponent<Entity>();
        // TODO: These lines should perhaps be moved somewhere inside Entity itself, they need to run before even Awake is called
        if(!AIData.entities.Contains(entity))
        {
            AIData.entities.Add(entity);
        }
        entity.sectorMngr = this;
        entity.faction = data.faction;
        entity.spawnPoint = data.position;
        entity.blueprint = blueprint;

        if(entity as AirCraft && data.patrolPath != null && data.patrolPath.waypoints != null && data.patrolPath.waypoints.Count > 0)
        {
            // patrolling
            (entity as AirCraft).GetAI().setPath(data.patrolPath, null, true);
        }

        if(data.ID == "" || data.ID == null || (objects.ContainsKey(data.ID) && !objects.ContainsValue(gObj)))
        {
            data.ID = objects.Count.ToString();
        }
        entity.ID = data.ID;

        if (data.dialogueID != "")
        {
            entity.dialogue = ResourceManager.GetAsset<Dialogue>(data.dialogueID);
        }

        if(!objects.ContainsKey(data.ID)) objects.Add(data.ID, gObj);
        return entity;
    }

    List<(EntityBlueprint, Sector.LevelEntity, int, float)> bgSpawns = new List<(EntityBlueprint, Sector.LevelEntity, int, float)>();
    
    private float bgSpawnTimer = 0;
    void loadSector()
    {
        #if UNITY_EDITOR
        if(Input.GetKey(KeyCode.LeftShift)) {

            // What does this do?

            SectorCreatorMouse.SectorData data = new SectorCreatorMouse.SectorData();
            data.platformjson = JsonUtility.ToJson(current.platforms);
            data.sectorjson = JsonUtility.ToJson(current);
            current.name = "SavedSector";
            //current.platforms = "SavedSectorPlatform";
            // var x = JsonUtility.ToJson(data);
		    // string path = Application.streamingAssetsPath + "\\Sectors\\" + "SavedSector";
		    // System.IO.File.WriteAllText(path, x);
		    // System.IO.Path.ChangeExtension(path, ".json");            
            UnityEditor.AssetDatabase.CreateAsset(current, "Assets/SavedSector.asset");
            //UnityEditor.AssetDatabase.CreateAsset(current.platform, "Assets/SavedSectorPlatform.asset");
        }
#endif

        //unload previous sector
        UnloadCurrentSector();

        //load new sector
        if(player) {
            // player has seen this sector now
            if (player.cursave.sectorsSeen == null)
                player.cursave.sectorsSeen = new List<string>();

            if (!player.cursave.sectorsSeen.Contains(current.sectorName))
                player.cursave.sectorsSeen.Add(current.sectorName);
            player.ResetPower();
            if(!objects.ContainsKey("player")) objects.Add("player", player.gameObject);
            player.sectorMngr = this;
            if(player.alerter) player.alerter.showMessage("Entering sector: " + current.sectorName);
        }


        for(int i = 0; i < current.entities.Length; i++)
        {
            // check if it is a character
            foreach(var ch in characters)
            {
                if(ch.ID == current.entities[i].ID)
                {
                    var skipTag = false;
                    foreach(var oj in objects)
                    {
                        if(oj.Value.GetComponentInChildren<Entity>() && oj.Value.GetComponentInChildren<Entity>().ID == ch.ID)
                        {
                            skipTag = true;
                            break;
                        }
                    }

                    if(skipTag) continue;
                    var print = ScriptableObject.CreateInstance<EntityBlueprint>();
                    JsonUtility.FromJsonOverwrite(ch.blueprintJSON, print);
                    print.intendedType = EntityBlueprint.IntendedType.ShellCore;
                    current.entities[i].name = ch.name;
                    current.entities[i].faction = ch.faction;
                    SpawnEntity(print, current.entities[i]);
                    continue;
                }
            }
            Object obj = ResourceManager.GetAsset<Object>(current.entities[i].assetID);

            if(obj is GameObject)
            {
                GameObject gObj = Instantiate(obj as GameObject);
                if(!gObj.GetComponent<EnergyRock>())
                    gObj.GetComponent<SpriteRenderer>().color = FactionManager.GetFactionColor(current.entities[i].faction);
                gObj.transform.position = current.entities[i].position;
                gObj.name = current.entities[i].name;
                if(gObj.GetComponent<ShardRock>()) {
                    gObj.GetComponent<ShardRock>().tier = int.Parse(current.entities[i].vendingID);
                }
                objects.Add(current.entities[i].ID, gObj);
            }
            else if(obj is EntityBlueprint)
            {
                var copy = Instantiate(obj);
                if((obj as EntityBlueprint).dialogue)
                {
                    (copy as EntityBlueprint).dialogue = Instantiate((obj as EntityBlueprint).dialogue);
                }
                SpawnEntity(copy as EntityBlueprint, current.entities[i]);
            }
        }

        //land platforms
        lpg.SetColor(current.backgroundColor + new Color(0.5F, 0.5F, 0.5F));

        Vector2 center = new Vector2(current.bounds.x + current.bounds.w / 2, current.bounds.y - current.bounds.h / 2);

        if (current.platform) // Old data
        {
            lpg.BuildTiles(current.platform, center);
        }
        else if (current.platformData.Length > 0)
        {
            GameObject[] prefabs = new GameObject[LandPlatformGenerator.prefabNames.Length];
            for (int i = 0; i < LandPlatformGenerator.prefabNames.Length; i++)
            {
                prefabs[i] = ResourceManager.GetAsset<GameObject>(LandPlatformGenerator.prefabNames[i]);
            }

            float tileSize = prefabs[0].GetComponent<SpriteRenderer>().bounds.size.x;
            lpg.tileSize = tileSize;

            var cols = current.bounds.h / (int)tileSize;
            var rows = current.bounds.w / (int)tileSize;

            Vector2 offset = new Vector2
            {
                x = center.x - tileSize * (rows - 1) / 2F,
                y = center.y + tileSize * (cols - 1) / 2F
            };

            lpg.Offset = offset;

            current.platforms = new GroundPlatform[current.platformData.Length];
            for (int i = 0; i < current.platformData.Length; i++)
            {
                var plat = new GroundPlatform(current.platformData[i], prefabs, lpg);
                current.platforms[i] = plat;
            }
            lpg.groundPlatforms = current.platforms;
        }

        // Restart particle and background effects to new skin if necessary
        if(RectangleEffectScript.currentSkin != current.rectangleEffectSkin)
        {
            RectangleEffectScript.currentSkin = current.rectangleEffectSkin;
            foreach(var rect in RectangleEffectScript.instances) if(rect) rect.Start();
        }
        if(BackgroundScript.currentSkin != current.backgroundTileSkin)
        {
            BackgroundScript.currentSkin = current.backgroundTileSkin;
            BackgroundScript.instance.setColor(SectorColors.colors[5], true);
            BackgroundScript.instance.Restart();
        }


        //sector color
        background.setColor(current.backgroundColor);
        //Camera.main.backgroundColor = current.backgroundColor / 2F;
        //sector borders
        foreach(var sector in sectors)
        {
            if(minimapSectorBorders != null && minimapSectorBorders[sector]) minimapSectorBorders[sector].enabled = minimapSectorBorders != null 
                && minimapSectorBorders.ContainsKey(sector) && player.cursave.sectorsSeen.Contains(sector.sectorName);
        }

        sectorBorders.enabled = true;
        sectorBorders.SetPositions(new Vector3[]{
            new Vector3(current.bounds.x, current.bounds.y, 0),
            new Vector3(current.bounds.x + current.bounds.w, current.bounds.y, 0),
            new Vector3(current.bounds.x + current.bounds.w, current.bounds.y - current.bounds.h, 0),
            new Vector3(current.bounds.x, current.bounds.y - current.bounds.h, 0)
        });
        sectorBorders.startColor = sectorBorders.endColor = new Color32((byte)85, (byte)100, (byte)85, (byte)255);
        battleZone.enabled = false;
        siegeZone.enabled = false;
        // sector type things
        switch(current.type)
        {
            case Sector.SectorType.BattleZone:
                //battle zone things
                battleZone.enabled = true;
                battleZone.sectorName = current.sectorName;
                if(player) {
                    var playerComp = player.GetComponent<PlayerCore>();
                    battleZone.AddTarget(playerComp);
                    if(carriers.ContainsKey(playerComp.faction))
                        playerComp.SetCarrier(carriers[playerComp.faction]);
                    foreach(var partyMember in PartyManager.instance.partyMembers)
                    {
                        partyMember.GetAI().setMode(AirCraftAI.AIMode.Battle);
                        battleZone.AddTarget(partyMember);
                        if(carriers.ContainsKey(partyMember.faction))
                            partyMember.SetCarrier(carriers[partyMember.faction]);
                    }
                }
                for (int i = 0; i < current.targets.Length; i++)
                {
                    if(objects[current.targets[i]].GetComponent<ShellCore>())
                    {
                        // set the carrier of the shellcore to the associated faction's carrier
                        ShellCore shellcore = objects[current.targets[i]].GetComponent<ShellCore>();
                        if(carriers.ContainsKey(shellcore.faction))
                            shellcore.SetCarrier(carriers[shellcore.faction]);
                    }
                    battleZone.AddTarget(objects[current.targets[i]].GetComponent<Entity>());
                }
                battleZone.UpdateCounters();
                break;
            case Sector.SectorType.Haven:
            case Sector.SectorType.Capitol:
                player.spawnPoint = new Vector2(current.bounds.x + current.bounds.w / 2, current.bounds.y - current.bounds.h / 2);
                break;
            case Sector.SectorType.SiegeZone:
                siegeZone.enabled = true;
                siegeZone.sectorName = current.sectorName;
                foreach(var wave in JsonUtility.FromJson<WaveSet>(File.ReadAllText(resourcePath + "\\Waves\\" + current.waveSetPath + ".json")).waves)
                {
                    siegeZone.waves.Enqueue(wave);
                }

                for (int i = 0; i < current.targets.Length; i++)
                {
                    siegeZone.AddTarget(objects[current.targets[i]].GetComponent<Entity>());
                }
                
                siegeZone.players.Add(PlayerCore.Instance);
                break;
            default:
                break;
        }

        if(current.backgroundSpawns != null)
            // background spawns
            foreach(var bgSpawn in current.backgroundSpawns)
            {
                bgSpawns.Add((GetBlueprintOfLevelEntity(bgSpawn.entity), bgSpawn.entity, bgSpawn.timePerSpawn, bgSpawn.radius));
            }

        // music
        PlayCurrentSectorMusic();

        if(info) info.showMessage("Entering sector '" + current.sectorName + "'");
        if (OnSectorLoad != null)
            OnSectorLoad.Invoke(current.sectorName);
    }

    private void UnloadCurrentSector()
    {
        var remainingObjects = new Dictionary<string, GameObject>();
        foreach (var obj in objects)
        {
            if (player && (!player.GetTractorTarget() || (player.GetTractorTarget() && obj.Value != player.GetTractorTarget().gameObject))
                && obj.Value != player.gameObject)
            {
                var skipTag = false;
                if (obj.Value && obj.Value.GetComponentInChildren<Entity>())
                {
                    foreach (var ch in characters)
                    {
                        if (obj.Value.GetComponentInChildren<Entity>().ID == ch.ID)
                        {
                            skipTag = true;
                            break;
                        }
                    }
                    if (!skipTag && AIData.entities.Contains(obj.Value.GetComponentInChildren<Entity>()))
                    {
                        AIData.entities.Remove(obj.Value.GetComponentInChildren<Entity>());
                    }
                }
                if (!skipTag)
                    Destroy(obj.Value);
                else remainingObjects.Add(obj.Key, obj.Value);
            }
            else remainingObjects.Add(obj.Key, obj.Value); // add to persistent objects since the object list should start only with characters
        }

        Dictionary<string, GameObject> tmp = new Dictionary<string, GameObject>();
        foreach (var obj in persistentObjects)
        {
            if (player && obj.Value && (!player.GetTractorTarget() || (player.GetTractorTarget() && obj.Value != player.GetTractorTarget().gameObject))
                && obj.Value != player.gameObject && !(player.unitsCommanding.Contains(obj.Value.GetComponent<Drone>() as IOwnable)
                && Vector3.SqrMagnitude(obj.Value.transform.position - player.transform.position) < 100))
            {
                Destroy(obj.Value);
            }
            else if (obj.Value) tmp.Add(obj.Key, obj.Value);
        }

        persistentObjects = tmp;

        foreach (ShellPart part in AIData.strayParts)
        {
            if (part && !(player && player.GetTractorTarget() && player.GetTractorTarget().GetComponent<ShellPart>() == part))
            {
                Destroy(part.gameObject);
            }
        }
        AIData.strayParts.Clear();

        // Add the player's tractored part back so it gets deleted if the player doesn't tractor it through
        // to another sector
        if ((player && player.GetTractorTarget() != null && player.GetTractorTarget().GetComponent<ShellPart>()))
            AIData.strayParts.Add(player.GetTractorTarget().GetComponent<ShellPart>());
        objects = remainingObjects;

        // reset stations and carriers

        stations.Clear();
        carriers.Clear();

        // reset background spawns
        bgSpawnTimer = 0;
        bgSpawns.Clear();

        lpg.Unload();
    }

    public static EntityBlueprint GetBlueprintOfLevelEntity(Sector.LevelEntity entity)
    {
        if(entity.assetID == "shellcore_blueprint")
        {
            EntityBlueprint blueprint = ScriptableObject.CreateInstance<EntityBlueprint>();
            JsonUtility.FromJsonOverwrite(entity.blueprintJSON, blueprint);
            return blueprint;
        }
        else 
        {
            return ResourceManager.GetAsset<EntityBlueprint>(entity.assetID);
        }
    }

    public void PlayCurrentSectorMusic() 
    {
        if(current.hasMusic)
        {
            AudioManager.PlayMusic(current.musicID);
        }
        else 
        {
            AudioManager.StopMusic();
        }
    }

    public void InsertPersistentObject(string key, GameObject gameObject) {
        persistentObjects.Add(key + uniqueIDInt++, gameObject);
    }

    public GameObject GetObject(string name) {
        Debug.Log("Getting object: '" + name + "'");
        foreach(var pair in objects) {
            if(pair.Value == null)
            {
                continue;
            }
            if(pair.Value.name == name) return pair.Value;
        }
        return null;
    }

    public Entity GetEntity(string ID)
    {
        // Debug.Log("Getting entity with ID: '" + ID + "'");
        foreach(var pair in objects) {
            if(pair.Value == null)
            {
                continue;
            }
            if(pair.Value.GetComponent<Entity>()?.ID == ID) return pair.Value.GetComponent<Entity>();
        }
        return null;
    }

    public void RemoveObject(string name)
    {
        if(objects.ContainsKey(name))
            objects.Remove(name);
    }

    public void Clear()
    {
        sectors.Clear();
        UnloadCurrentSector();
        foreach (var border in minimapSectorBorders)
        {
            Destroy(border.Value.gameObject);
        }
    }
}
