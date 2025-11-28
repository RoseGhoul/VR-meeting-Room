using TMPro;
using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PresenterManager : NetworkBehaviour
{
    public RawImage rawImage;
    public bool present = false;
    [SerializeField] int maxPresenters;
    ScreenReceiver currentPresenter;
    [SerializeField] TextMeshProUGUI presentingText;

    void Update()
    {
        // if(IsServer) return;
        if (present && currentPresenter != null)
            rawImage.texture = currentPresenter.rawImage.texture;
        else
            rawImage.texture = Texture2D.blackTexture;
    }
    public void setPresenter(NetworkObjectReference networkObjectReference)
    {
        if (networkObjectReference.TryGet(out NetworkObject netObj))
        {
            currentPresenter = netObj.GetComponent<ScreenReceiver>();
            setPresentClientRpc(networkObjectReference);
        }
        else
        {
            Debug.LogWarning("Invalid NetworkObjectReference received!");
        }
    }
    [ClientRpc]
    public void setPresentClientRpc(NetworkObjectReference networkObjectReference)
    {
        if (networkObjectReference.TryGet(out NetworkObject netObj))
        {
            currentPresenter = netObj.GetComponent<ScreenReceiver>();
            // present = true;
        }
        else
        {
            Debug.LogWarning("Invalid NetworkObjectReference received!");
        }
    }
    public void setPresent(bool isPresent)
    {
        present = isPresent;
        presentingText.text = present ? "Stop Presenting" : "Start Presenting";
        setPresentClientRpc(isPresent);
    }
    [ClientRpc]
    public void setPresentClientRpc(bool isPresent)
    {
        present = isPresent;
    }
    [ClientRpc(RequireOwnership = false)]
    public void kickClientRpc(ulong ClientID)
    {
        if (NetworkManager.Singleton.LocalClientId == ClientID)
        {
            SceneManager.LoadScene("MeetingRoom");
            VivoxService.Instance.LogoutAsync();
            NetworkManager.Singleton.DisconnectClient(ClientID);
            Debug.Log($"[Client {ClientID}] Kicked from server.");
        }
    }
}
