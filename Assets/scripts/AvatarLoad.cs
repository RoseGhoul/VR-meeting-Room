using UnityEngine;
using Unity.Netcode;
using TMPro;
using Unity.Collections;
using GLTFast.Schema;
using UnityEngine.SceneManagement;

public class AvatarLoaderWithConstraint : NetworkBehaviour
{
    [Header("Participant Info")]
    public ParticipentData participentData;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI[] nameTag= new TextMeshProUGUI[4];
    private Transform chairPosition;

    private NetworkVariable<FixedString64Bytes> playerName =
        new NetworkVariable<FixedString64Bytes>(
            "",
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
    [SerializeField] GameObject[] avatars = new GameObject[4];

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        playerName.OnValueChanged += OnNameChanged;

        if (IsServer)
        {
            if (string.IsNullOrEmpty(playerName.Value.ToString()))
                playerName.Value = $"Seat_{OwnerClientId}";
        }

        OnNameChanged("", playerName.Value);

        if (IsOwner)
        {
            Debug.Log($"[Client {OwnerClientId}] OnNetworkSpawn called.");

            var button = FindAnyObjectByType<ButtonBehaviourAdder>();
            if (button != null)
            {
                participentData = new ParticipentData(button.NameField, NetworkManager.Singleton.LocalClientId, GetComponent<NetworkObject>());
                SelectAvatarServerRpc(button.avatarIndex);
                GetComponent<ScreenReceiver>().streamerIP = button.IP;
                button.DactivateUi();
                setServerButtonsServerRpc(new NetworkObjectReference(GetComponent<NetworkObject>()), participentData.Name);
                AssignChairServerRpc(new NetworkObjectReference(GetComponent<NetworkObject>()));
                SetNameServerRpc(participentData.Name);
            }
        }
    }
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
    [ServerRpc(RequireOwnership = false)]
    void setServerButtonsServerRpc(NetworkObjectReference objRef, string name)
    {
        Debug.Log($"[Server] Adding button for client {name}");
        FindAnyObjectByType<ServerScript>().addButton(objRef, name);
    }

    [ServerRpc(RequireOwnership = false)]
    void SelectAvatarServerRpc(int index)
    {
        for (int i = 0; i < avatars.Length; i++)
        {
            if (avatars[i] != null)
            {
                avatars[i].SetActive(i == index);
            }
        }
        SelectAvatarClientRpc(index);
    }
    [ClientRpc(RequireOwnership = false)]
    void SelectAvatarClientRpc(int index)
    {
        for (int i = 0; i < avatars.Length; i++)
        {
            if (avatars[i] != null)
            {
                avatars[i].SetActive(i == index);
            }
        }
    }
    [ServerRpc(RequireOwnership = false)]
    private void AssignChairServerRpc(NetworkObjectReference objRef)
    {
        if (!objRef.TryGet(out NetworkObject netObj))
            return;

        Debug.Log($"[Server] Assigning chair for client {netObj.OwnerClientId}");

        chairPosition = FindAnyObjectByType<ChairPositions>().getChair(netObj.gameObject);

        if (chairPosition != null)
        {
            netObj.transform.position = chairPosition.position;
            netObj.transform.rotation = chairPosition.rotation;

            UpdateChairPositionClientRpc(objRef, chairPosition.position, chairPosition.rotation);
        }
        else
        {
            Debug.LogWarning($"[Server] No chair found for client {netObj.OwnerClientId}");
        }
    }

    [ClientRpc(RequireOwnership = false)]
    private void UpdateChairPositionClientRpc(NetworkObjectReference objRef, Vector3 pos, Quaternion rot)
    {
        if (!objRef.TryGet(out NetworkObject netObj)) return;

        Debug.Log($"[Client] Updating chair position for ClientID: {netObj.OwnerClientId}");

        netObj.transform.position = pos;
        netObj.transform.rotation = rot;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetNameServerRpc(string newName)
    {
        if (string.IsNullOrEmpty(newName))
            newName = $"Player_{OwnerClientId}";

        playerName.Value = newName;
        Debug.Log($"[Server] Name set for ClientID {OwnerClientId}: {newName}");
    }

    private void OnNameChanged(FixedString64Bytes oldValue, FixedString64Bytes newValue)
    {
        foreach (var nameTag in nameTag)
        {
            if (nameTag != null)
            {
                nameTag.text = newValue.ToString();
                Debug.Log($"[Client] Name updated to: {newValue}");
            }
            else
            {
                Debug.LogWarning("[Client] NameTag not found to update name text!");
            }
        }
    }
}

public class ParticipentData
{
    public string Name;
    public ulong ClientID;
    public NetworkObject networkObject;

    public ParticipentData(string name, ulong clientID, NetworkObject netObj)
    {
        Name = name;
        ClientID = clientID;
        networkObject = netObj;
    }
}
