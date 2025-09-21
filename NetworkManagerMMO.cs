// Assets/uMMORPG/Scripts/NetworkManagerMMO.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Mirror;
using UnityEngine.Events;
using UnityEngine.Rendering; // needed for GraphicsDeviceType (server/headless builds)
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum NetworkState { Offline, Handshake, Lobby, World }

[Serializable] public class UnityEventCharactersAvailableMsg : UnityEvent<CharactersAvailableMsg> {}
[Serializable] public class UnityEventCharacterCreateMsgPlayer : UnityEvent<CharacterCreateMsg, Player> {}
[Serializable] public class UnityEventStringGameObjectNetworkConnectionCharacterSelectMsg : UnityEvent<string, GameObject, NetworkConnection, CharacterSelectMsg> {}
[Serializable] public class UnityEventCharacterDeleteMsg : UnityEvent<CharacterDeleteMsg> {}
[Serializable] public class UnityEventNetworkConnection : UnityEvent<NetworkConnection> {}

[RequireComponent(typeof(Database))]
[DisallowMultipleComponent]
public partial class NetworkManagerMMO : NetworkManager
{
    public NetworkState state = NetworkState.Offline;

    public Dictionary<NetworkConnection, string> lobby = new Dictionary<NetworkConnection, string>();

    [Header("UI")]
    public UIPopup uiPopup;

    [Serializable]
    public class ServerInfo
    {
        public string name;
        public string ip;
    }
    public List<ServerInfo> serverList = new List<ServerInfo>() {
        new ServerInfo{name="Local", ip="localhost"}
    };

    [Header("Logout")]
    [Tooltip("Players shouldn't be able to log out instantly to flee combat. There should be a delay.")]
    public float combatLogoutDelay = 5;
    [HideInInspector] public bool changingCharacters = false;
    [Header("Character Selection")]
    public int selection = -1;
    public Transform[] selectionLocations;
    public Transform selectionCameraLocation;
    [HideInInspector] public List<Player> playerClasses = new List<Player>();

    [Header("Database")]
    public int characterLimit = 4;
    public int characterNameMaxLength = 16;
    public float saveInterval = 60f;

    [Header("Events")]
    public UnityEvent onStartClient;
    public UnityEvent onStopClient;
    public UnityEvent onStartServer;
    public UnityEvent onStopServer;
    public UnityEventNetworkConnection onClientConnect;
    public UnityEventNetworkConnection onServerConnect;
    public UnityEventCharactersAvailableMsg onClientCharactersAvailable;
    public UnityEventCharacterCreateMsgPlayer onServerCharacterCreate;
    public UnityEventStringGameObjectNetworkConnectionCharacterSelectMsg onServerCharacterSelect;
    public UnityEventCharacterDeleteMsg onServerCharacterDelete;
    public UnityEventNetworkConnection onClientDisconnect;
    public UnityEventNetworkConnection onServerDisconnect;

    [HideInInspector] public CharactersAvailableMsg charactersAvailableMsg;

    [Header("Client Rendering")]
    [Tooltip("If true, the options below will apply to client builds (including Host client).")]
    public bool clientFpsOverrideEnabled = true;

    public enum ClientFpsMode { Unlimited, VSync, TargetFps }
    [Tooltip("How to control client framerate.")]
    public ClientFpsMode clientFpsMode = ClientFpsMode.VSync;

    [Tooltip("Used when ClientFpsMode=TargetFps.")]
    public int clientTargetFps = 120;

    [Tooltip("Used when ClientFpsMode=VSync. 0=Off, 1=Every V-Blank, 2=Every Second V-Blank.")]
    public int clientVSyncCount = 1;

    [Tooltip("Allow overriding client FPS settings via command line (e.g., -client.maxfps=144, -client.vsync=0).")]
    public bool clientAllowCommandLineOverride = true;

    static readonly Regex allowedNameRegex = new Regex(@"^[a-zA-Z0-9_]+$", RegexOptions.Compiled);

    public virtual bool IsAllowedCharacterName(string characterName)
    {
        return characterName.Length <= characterNameMaxLength &&
               allowedNameRegex.IsMatch(characterName);
    }

    public static Transform GetNearestStartPosition(Vector3 from) =>
        Utils.GetNearestTransform(startPositions, from);

    public List<Player> FindPlayerClasses()
    {
        List<Player> classes = new List<Player>();
        foreach (GameObject prefab in spawnPrefabs)
        {
            Player player = prefab.GetComponent<Player>();
            if (player != null)
                classes.Add(player);
        }
        return classes;
    }

    public override void Awake()
    {
        base.Awake();
        playerClasses = FindPlayerClasses();
    }

    public override void Update()
    {
        base.Update();
        if (NetworkClient.localPlayer != null)
            state = NetworkState.World;
    } 

    public void ServerSendError(NetworkConnection conn, string error, bool disconnect)
    {
        conn.Send(new ErrorMsg { text = error, causesDisconnect = disconnect });
    }

    void OnClientError(ErrorMsg message)
    {
        Debug.Log("OnClientError: " + message.text);
#if !UNITY_SERVER || UNITY_EDITOR
        if (uiPopup != null) uiPopup.Show(message.text);
#endif
        if (message.causesDisconnect)
        {
            NetworkClient.connection.Disconnect();
            if (NetworkServer.active) StopHost();
        }
    } 

    public override void OnStartClient()
    {
        NetworkClient.RegisterHandler<ErrorMsg>(OnClientError, false);
        NetworkClient.RegisterHandler<CharactersAvailableMsg>(OnClientCharactersAvailable);
        onStartClient.Invoke();

#if !UNITY_SERVER || UNITY_EDITOR
        ApplyClientFpsPolicy();
#endif
    } 

    public override void OnStartServer()
    {
        Database.singleton.Connect();
        NetworkServer.RegisterHandler<CharacterCreateMsg>(OnServerCharacterCreate);
        NetworkServer.RegisterHandler<CharacterSelectMsg>(OnServerCharacterSelect);
        NetworkServer.RegisterHandler<CharacterDeleteMsg>(OnServerCharacterDelete);
        InvokeRepeating(nameof(SavePlayers), saveInterval, saveInterval);
        onStartServer.Invoke();
    } 

    public override void OnStopClient()
    {
        onStopClient.Invoke();
    } 

    public override void OnStopServer()
    {
        CancelInvoke(nameof(SavePlayers));
        onStopServer.Invoke();
    } 

    public override void OnClientConnect()
    {
        onClientConnect.Invoke(NetworkClient.connection);
    } 

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        if (lobby.TryGetValue(conn, out string account))
        {
            conn.Send(MakeCharactersAvailableMessage(account));
            onServerConnect.Invoke(conn);
        }
        else
        {
            Debug.LogWarning($"OnServerConnect: connection {conn} not found in lobby yet (handshake race?).");
        }
    } 

    public override void OnClientSceneChanged() { }

    CharactersAvailableMsg MakeCharactersAvailableMessage(string account)
    {
        List<Player> characters = new List<Player>();
        foreach (string characterName in Database.singleton.CharactersForAccount(account))
        {
            GameObject player = Database.singleton.CharacterLoad(characterName, playerClasses, true);
            characters.Add(player.GetComponent<Player>());
        }
        CharactersAvailableMsg message = new CharactersAvailableMsg();
        message.Load(characters);
        characters.ForEach(player => Destroy(player.gameObject));
        return message;
    } 

    void LoadPreview(GameObject prefab, Transform location, int selectionIndex, CharactersAvailableMsg.CharacterPreview character)
    {
        GameObject preview = Instantiate(prefab.gameObject, location.position, location.rotation);
        preview.transform.parent = location;
        Player player = preview.GetComponent<Player>();

        player.name = character.name;
        player.isGameMaster = character.isGameMaster;
        for (int i = 0; i < character.equipment.Length; ++i)
        {
            ItemSlot slot = character.equipment[i];
            player.equipment.slots.Add(slot);
            if (slot.amount > 0)
            {
                ((PlayerEquipment)player.equipment).RefreshLocation(i);
            }
        }

        preview.AddComponent<SelectableCharacter>();
        preview.GetComponent<SelectableCharacter>().index = selectionIndex;
    } 

    public void ClearPreviews()
    {
        selection = -1;
        foreach (Transform location in selectionLocations)
            if (location.childCount > 0)
                Destroy(location.GetChild(0).gameObject);
    } 

    void OnClientCharactersAvailable(CharactersAvailableMsg message)
    {
        charactersAvailableMsg = message;
        Debug.Log("characters available:" + charactersAvailableMsg.characters.Length);

        state = NetworkState.Lobby;

        ClearPreviews();

        for (int i = 0; i < charactersAvailableMsg.characters.Length; ++i)
        {
            CharactersAvailableMsg.CharacterPreview character = charactersAvailableMsg.characters[i];
            Player prefab = playerClasses.Find(p => p.name == character.className);
            if (prefab != null)
                LoadPreview(prefab.gameObject, selectionLocations[i], i, character);
            else
                Debug.LogWarning("Character Selection: no prefab found for class " + character.className);
        }

#if !UNITY_SERVER || UNITY_EDITOR
        if (Camera.main != null)
        {
            Camera.main.transform.position = selectionCameraLocation.position;
            Camera.main.transform.rotation = selectionCameraLocation.rotation;
        }
#endif
        onClientCharactersAvailable.Invoke(charactersAvailableMsg);
    } 

    public Transform GetStartPositionFor(string className)
    {
        foreach (Transform startPosition in startPositions)
        {
            NetworkStartPositionForClass spawn = startPosition.GetComponent<NetworkStartPositionForClass>();
            if (spawn != null &&
                spawn.playerPrefab != null &&
                spawn.playerPrefab.name == className)
                return spawn.transform;
        }
        return GetStartPosition();
    } 

    Player CreateCharacter(GameObject classPrefab, string characterName, string account, bool gameMaster)
    {
        Player player = Instantiate(classPrefab).GetComponent<Player>();
        player.name = characterName;
        player.account = account;
        player.className = classPrefab.name;
        player.transform.position = GetStartPositionFor(player.className).position;
        for (int i = 0; i < player.inventory.size; ++i)
        {
            player.inventory.slots.Add(i < player.inventory.defaultItems.Length
                ? new ItemSlot(new Item(player.inventory.defaultItems[i].item), player.inventory.defaultItems[i].amount)
                : new ItemSlot());
        }
        for (int i = 0; i < ((PlayerEquipment)player.equipment).slotInfo.Length; ++i)
        {
            EquipmentInfo info = ((PlayerEquipment)player.equipment).slotInfo[i];
            player.equipment.slots.Add(info.defaultItem.item != null
                ? new ItemSlot(new Item(info.defaultItem.item), info.defaultItem.amount)
                : new ItemSlot());
        }
        player.health.current = player.health.max;
        player.mana.current = player.mana.max;
        player.isGameMaster = gameMaster;
        return player;
    } 

    public override void OnServerAddPlayer(NetworkConnectionToClient conn) { Debug.LogWarning("Use the CharacterSelectMsg instead"); } // ref. :contentReference[oaicite:21]{index=21}

    void OnServerCharacterCreate(NetworkConnection conn, CharacterCreateMsg message)
    {
        if (lobby.ContainsKey(conn))
        {
            if (IsAllowedCharacterName(message.name))
            {
                string account = lobby[conn];
                if (!Database.singleton.CharacterExists(message.name))
                {
                    if (Database.singleton.CharactersForAccount(account).Count < characterLimit)
                    {
                        if (0 <= message.classIndex && message.classIndex < playerClasses.Count)
                        {
                            if (message.gameMaster == false || conn == NetworkServer.localConnection)
                            {
                                Player player = CreateCharacter(playerClasses[message.classIndex].gameObject, message.name, account, message.gameMaster);
                                onServerCharacterCreate.Invoke(message, player);
                                Database.singleton.CharacterSave(player, false);
                                Destroy(player.gameObject);
                                conn.Send(MakeCharactersAvailableMessage(account));
                            }
                            else ServerSendError(conn, "insufficient permissions", false);
                        }
                        else ServerSendError(conn, "character invalid class", false);
                    }
                    else ServerSendError(conn, "character limit reached", false);
                }
                else ServerSendError(conn, "name already exists", false);
            }
            else ServerSendError(conn, "character name not allowed", false);
        }
        else ServerSendError(conn, "CharacterCreate: not in lobby", true);
    } 

    void OnServerCharacterSelect(NetworkConnectionToClient conn, CharacterSelectMsg message)
    {
        if (lobby.ContainsKey(conn))
        {
            string account = lobby[conn];
            List<string> characters = Database.singleton.CharactersForAccount(account);
            if (0 <= message.index && message.index < characters.Count)
            {
                GameObject go = Database.singleton.CharacterLoad(characters[message.index], playerClasses, false);
                NetworkServer.AddPlayerForConnection(conn, go);
                onServerCharacterSelect.Invoke(account, go, conn, message);
                lobby.Remove(conn);
            }
            else
            {
                Debug.Log("invalid character index: " + account + " " + message.index);
                ServerSendError(conn, "invalid character index", false);
            }
        }
        else
        {
            Debug.Log("CharacterSelect: not in lobby" + conn);
            ServerSendError(conn, "CharacterSelect: not in lobby", true);
        }
    } 

    void OnServerCharacterDelete(NetworkConnection conn, CharacterDeleteMsg message)
    {
        if (lobby.ContainsKey(conn))
        {
            string account = lobby[conn];
            List<string> characters = Database.singleton.CharactersForAccount(account);
            if (0 <= message.index && message.index < characters.Count)
            {
                Debug.Log("delete character: " + characters[message.index]);
                Database.singleton.CharacterDelete(characters[message.index]);
                onServerCharacterDelete.Invoke(message);
                conn.Send(MakeCharactersAvailableMessage(account));
            }
            else
            {
                Debug.Log("invalid character index: " + account + " " + message.index);
                ServerSendError(conn, "invalid character index", false);
            }
        }
        else
        {
            Debug.Log("CharacterDelete: not in lobby: " + conn);
            ServerSendError(conn, "CharacterDelete: not in lobby", true);
        }
    } 

    void SavePlayers()
    {
        Database.singleton.CharacterSaveMany(Player.onlinePlayers.Values);
        if (Player.onlinePlayers.Count > 0)
            Debug.Log("saved " + Player.onlinePlayers.Count + " player(s)");
    } 

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        float delay = 0;
        if (conn.identity != null)
        {
            Player player = conn.identity.GetComponent<Player>();
            delay = (float)player.remainingLogoutTime;
        }
        StartCoroutine(DoServerDisconnect(conn, delay));
    } 

    IEnumerator DoServerDisconnect(NetworkConnectionToClient conn, float delay)
    {
        if (delay > 0)
            yield return new WaitForSeconds(delay);

        if (conn.identity != null)
        {
            Database.singleton.CharacterSave(conn.identity.GetComponent<Player>(), false);
            Debug.Log("saved:" + conn.identity.name);
        }

        onServerDisconnect.Invoke(conn);
        lobby.Remove(conn);
        base.OnServerDisconnect(conn);
    } 

    public override void OnClientDisconnect()
    {
        Debug.Log("OnClientDisconnect");

#if !UNITY_SERVER || UNITY_EDITOR
        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.transform.parent != null)
            mainCamera.transform.SetParent(null);
        if (uiPopup != null) uiPopup.Show("Disconnected.");
#endif

        base.OnClientDisconnect();
        state = NetworkState.Offline;
        onClientDisconnect.Invoke(NetworkClient.connection);
    } 

    public static void Quit()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    } 
    
    public override void ConfigureHeadlessFrameRate()
    {
        if (IsHeadless())
            Application.targetFrameRate = sendRate;
    } 

#if !UNITY_SERVER || UNITY_EDITOR
    void ApplyClientFpsPolicy()
    {
        if (!clientFpsOverrideEnabled) return;

        // optional CLI overrides
        if (clientAllowCommandLineOverride)
        {
            if (TryGetArgInt("-client.maxfps", out int cliFps)) clientFpsMode = ClientFpsMode.TargetFps; // enforce target mode
            if (TryGetArgInt("-client.vsync", out int cliV)) clientVSyncCount = Mathf.Clamp(cliV, 0, 2);
            if (TryGetArgInt("-client.maxfps", out cliFps)) clientTargetFps = Mathf.Clamp(cliFps, -1, 1000);
        }

        switch (clientFpsMode)
        {
            case ClientFpsMode.Unlimited:
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1; // platform default / uncapped
                break;
            case ClientFpsMode.VSync:
                QualitySettings.vSyncCount = Mathf.Clamp(clientVSyncCount, 0, 2);
                Application.targetFrameRate = -1;
                break;
            case ClientFpsMode.TargetFps:
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = Mathf.Clamp(clientTargetFps, -1, 1000);
                break;
        }
    }

    static bool TryGetArgInt(string key, out int value)
    {
        value = 0;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; ++i)
        {
            if (args[i].StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                string raw = args[i].Contains("=") ? args[i].Substring(args[i].IndexOf('=') + 1) :
                              i + 1 < args.Length ? args[i + 1] : null;
                if (int.TryParse(raw, out value))
                    return true;
            }
        }
        return false;
    }
#endif
    static bool IsHeadless()
    {
#if UNITY_SERVER
    return true; 
#else
        return Application.isBatchMode ||
               SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
#endif
    }


    public override void OnValidate()
    {
        base.OnValidate();

        if (!Application.isPlaying && networkAddress != "")
            networkAddress = "Use the Server List below!";
        if (selectionLocations.Length != characterLimit)
        {
            Transform[] newArray = new Transform[characterLimit];
            for (int i = 0; i < Mathf.Min(characterLimit, selectionLocations.Length); ++i)
                newArray[i] = selectionLocations[i];
            selectionLocations = newArray;
        }
    } 
}
