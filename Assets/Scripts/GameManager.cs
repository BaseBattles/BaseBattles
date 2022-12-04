using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    // Singleton
    public static GameManager _instance;
    public static GameManager Instance => _instance;

    [SerializeField]
    private Transform lobbyListItemPrefab, lobbyList;
    [SerializeField]
    private TMP_InputField lobbyNameInput;

    private string _lobbyId;

    private RelayHostData _hostData;
    private RelayJoinData _joinData;

    // Setup events

    // Notify state update
    public UnityAction<string> UpdateState;
    // Notify Match found
    public UnityAction MatchFound;

    private void Awake()
    {
        // Just a basic singleton
        if (_instance is null)
        {
            _instance = this;
            return;
        }

        Destroy(this);
    }

    async void Start()
    {
        // Initialize unity services
        await UnityServices.InitializeAsync();
        // Setup events listeners
        SetupEvents();
        // Unity Login
        await SignInAnonymouslyAsync();
        // Subscribe to NetworkManager events
        NetworkManager.Singleton.OnClientConnectedCallback += ClientConnected;
    }

    private void ClientConnected(ulong id)
    {
        // Player with id connected to our session
        Debug.Log("Connected player with id: " + id);

        UpdateState?.Invoke("Player found!");
        MatchFound?.Invoke();
    }

    void SetupEvents()
    {
        AuthenticationService.Instance.SignedIn += () =>
        {
            // Shows how to get a playerID
            Debug.Log($"PlayerID: {AuthenticationService.Instance.PlayerId}");
            // Shows how to get an access token
            Debug.Log($"Access Token: {AuthenticationService.Instance.AccessToken}");
        };

        AuthenticationService.Instance.SignInFailed += (err) =>
        {
            Debug.LogError(err);
        };

        AuthenticationService.Instance.SignedOut += () =>
        {
            Debug.Log("Player signed out.");
        };
    }

    async Task SignInAnonymouslyAsync()
    {
        try
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("Sign in anonymously succeeded!");
        }
        catch (Exception ex)
        {
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
    }

    public async void QuickJoin()
    {
        Debug.Log("Looking for a lobby...");

        UpdateState?.Invoke("Looking for a match...");

        try
        {
            // Looking for a lobby

            // Add options to the matchmaking (mode, rank, etc..)
            QuickJoinLobbyOptions options = new QuickJoinLobbyOptions();

            // Quick-join a random lobby
            Lobby lobby = await Lobbies.Instance.QuickJoinLobbyAsync(options);

            Debug.Log("Joined lobby: " + lobby.Id);
            Debug.Log("Lobby Players: " + lobby.Players.Count);

            // Retrieve the Relay code previously set in the create match
            string joinCode = lobby.Data["joinCode"].Value;
            // Save Lobby ID for later uses
            _lobbyId = lobby.Id;

            Debug.Log("Received code: " + joinCode);

            JoinAllocation allocation = await Relay.Instance.JoinAllocationAsync(joinCode);

            // Create Object
            _joinData = new RelayJoinData
            {
                Key = allocation.Key,
                Port = (ushort)allocation.RelayServer.Port,
                AllocationID = allocation.AllocationId,
                AllocationIDBytes = allocation.AllocationIdBytes,
                ConnectionData = allocation.ConnectionData,
                HostConnectionData = allocation.HostConnectionData,
                IPv4Address = allocation.RelayServer.IpV4
            };

            // Set transport data
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
                _joinData.IPv4Address,
                _joinData.Port,
                _joinData.AllocationIDBytes,
                _joinData.Key,
                _joinData.ConnectionData,
                _joinData.HostConnectionData);

            // Finally start the client
            NetworkManager.Singleton.StartClient();

            // Trigger events
            UpdateState?.Invoke("Match found!");
            MatchFound?.Invoke();
        }
        catch (LobbyServiceException e)
        {
            // We don't find any lobby
            UpdateState?.Invoke("Cannot find a lobby");
            Debug.Log("Cannot find a lobby: " + e);
        }
    }

    public async void CreateLobby()
    {
        Debug.Log(lobbyNameInput.text);
        if (lobbyNameInput.text == "")
        {
            Debug.Log("Enter a lobby name");
            return;
        }

        Debug.Log("Creating a new lobby...");

        UpdateState?.Invoke("Creating a new match...");

        // External connections
        int maxConnections = 7;

        try
        {
            // Create RELAY object
            Allocation allocation = await Relay.Instance.CreateAllocationAsync(maxConnections);
            _hostData = new RelayHostData
            {
                Key = allocation.Key,
                Port = (ushort)allocation.RelayServer.Port,
                AllocationID = allocation.AllocationId,
                AllocationIDBytes = allocation.AllocationIdBytes,
                ConnectionData = allocation.ConnectionData,
                IPv4Address = allocation.RelayServer.IpV4
            };

            // Retrieve JoinCode
            _hostData.JoinCode = await Relay.Instance.GetJoinCodeAsync(allocation.AllocationId);

            string lobbyName = lobbyNameInput.text;
            int maxPlayers = 8;
            CreateLobbyOptions options = new CreateLobbyOptions();
            options.IsPrivate = false;

            // Put the JoinCode in the lobby data, visible by every member
            options.Data = new Dictionary<string, DataObject>()
            {
                {
                    "joinCode", new DataObject(
                        visibility: DataObject.VisibilityOptions.Member,
                        value: _hostData.JoinCode)
                },
            };

            // Create the lobby
            var lobby = await Lobbies.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);

            // Save Lobby ID for later uses
            _lobbyId = lobby.Id;

            Debug.Log("Created lobby: " + lobby.Id);

            // Heartbeat the lobby every 15 seconds.
            StartCoroutine(HeartbeatLobbyCoroutine(lobby.Id, 15));

            // Now that RELAY and LOBBY are set...

            // Set Transports data
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
                _hostData.IPv4Address,
                _hostData.Port,
                _hostData.AllocationIDBytes,
                _hostData.Key,
                _hostData.ConnectionData);

            // Finally start host
            NetworkManager.Singleton.StartHost();

            UpdateState?.Invoke("Waiting for players...");
        }
        catch (LobbyServiceException e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async void LeaveLobby()
    {
        if (_lobbyId == null)
        {
            Debug.Log($"Not in lobby");
            return;
        }

        try
        {
            string playerId = AuthenticationService.Instance.PlayerId;
            await LobbyService.Instance.RemovePlayerAsync(_lobbyId, playerId);
            Debug.Log($"Left lobby {_lobbyId}");
            _lobbyId = null;
            NetworkManager.Singleton.Shutdown();
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public async void RefreshLobbyList()
    {
        foreach (Transform child in lobbyList)
        {
            Destroy(child.gameObject);
        }

        // Query for existing lobbies
        // Use filters to only return lobbies which match specific conditions
        // You can only filter on built-in properties (Ex: AvailableSlots) or indexed custom data (S1, N1, etc.)
        // Take a look at the API for other built-in fields you can filter on
        List<QueryFilter> queryFilters = new List<QueryFilter>
        {
            // Let's search for games with open slots (AvailableSlots greater than 0)
            new QueryFilter(
                field: QueryFilter.FieldOptions.AvailableSlots,
                op: QueryFilter.OpOptions.GT,
                value: "0"),
        };
        // Query results can also be ordered
        // The query API supports multiple "order by x, then y, then..." options
        // Order results by available player slots (least first), then by lobby age, then by lobby name
        List<QueryOrder> queryOrdering = new List<QueryOrder>
        {
            new QueryOrder(true, QueryOrder.FieldOptions.AvailableSlots),
            new QueryOrder(false, QueryOrder.FieldOptions.Created),
            new QueryOrder(false, QueryOrder.FieldOptions.Name),
        };
        // Call the Query API
        QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions()
        {
            Count = 20, // Override default number of results to return
            Filters = queryFilters,
            Order = queryOrdering,
        });
        List<Lobby> foundLobbies = response.Results;
        if (foundLobbies.Any()) // List the lobbies if any are found
        {

            // Let's print info about the lobbies we found
            foreach (var lobby in foundLobbies)
            {
                var lobbyListItem = Instantiate(lobbyListItemPrefab, lobbyList.position, Quaternion.identity, lobbyList).transform;
                lobbyListItem.Translate(0f, 25f - 50f * lobbyList.childCount, 0f);
                lobbyListItem.Find("Lobby name").GetComponent<TMP_Text>().text = lobby.Name;
                lobbyListItem.Find("Join lobby").GetComponent<Button>().onClick.AddListener(() => { JoinLobby(lobby.Id); });
            }

            Debug.Log("Found lobbies:\n" + JsonConvert.SerializeObject(foundLobbies));
        }
        else
        {
            Debug.Log("no lobbies found");
        }
    }

    public async void JoinLobby(string lobbyId)
    {
        try
        {
            // Try to join the lobby
            // Player is optional because the service can pull the player data from the auth token
            // However, if your player has custom data, you will want to pass the Player object into this call
            // This will save you having to do a Join call followed by an UpdatePlayer call
            var lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);

            Debug.Log("Joined lobby: " + lobby.Id);
            Debug.Log("Lobby Players: " + lobby.Players.Count);

            // Retrieve the Relay code previously set in the create match
            string joinCode = lobby.Data["joinCode"].Value;
            // Save Lobby ID for later uses
            _lobbyId = lobby.Id;
            
            Debug.Log("Received code: " + joinCode);

            JoinAllocation allocation = await Relay.Instance.JoinAllocationAsync(joinCode);

            // Create Object
            _joinData = new RelayJoinData
            {
                Key = allocation.Key,
                Port = (ushort)allocation.RelayServer.Port,
                AllocationID = allocation.AllocationId,
                AllocationIDBytes = allocation.AllocationIdBytes,
                ConnectionData = allocation.ConnectionData,
                HostConnectionData = allocation.HostConnectionData,
                IPv4Address = allocation.RelayServer.IpV4
            };

            // Set transport data
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
                _joinData.IPv4Address,
                _joinData.Port,
                _joinData.AllocationIDBytes,
                _joinData.Key,
                _joinData.ConnectionData,
                _joinData.HostConnectionData);

            // Finally start the client
            NetworkManager.Singleton.StartClient();

            // Trigger events
            UpdateState?.Invoke("Match found!");
            MatchFound?.Invoke();
        }
        catch (LobbyServiceException e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    IEnumerator HeartbeatLobbyCoroutine(string lobbyId, float waitTimeSeconds)
    {
        var delay = new WaitForSecondsRealtime(waitTimeSeconds);
        while (true)
        {
            Lobbies.Instance.SendHeartbeatPingAsync(lobbyId);
            Debug.Log("Lobby Heartbit");
            yield return delay;
        }
    }

    private void OnDestroy()
    {
        // We need to delete the lobby when we're not using it
        Lobbies.Instance.DeleteLobbyAsync(_lobbyId);
    }

    // RelayHostData represents the necessary informations for a Host to host a game on a Relay
    public struct RelayHostData
    {
        public string JoinCode;
        public string IPv4Address;
        public ushort Port;
        public Guid AllocationID;
        public byte[] AllocationIDBytes;
        public byte[] ConnectionData;
        public byte[] Key;
    }

    // RelayHostData represents the necessary informations for a Host to host a game on a Relay
    public struct RelayJoinData
    {
        public string JoinCode;
        public string IPv4Address;
        public ushort Port;
        public Guid AllocationID;
        public byte[] AllocationIDBytes;
        public byte[] ConnectionData;
        public byte[] HostConnectionData;
        public byte[] Key;
    }
}
