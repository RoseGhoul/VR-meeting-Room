using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Android;

public class ButtonBehaviourAdder : MonoBehaviour
{
    [SerializeField] Button buttonClient;
    [SerializeField] Button buttonHost;
    [SerializeField] TMP_InputField codeInputField;
    [SerializeField] TextMeshProUGUI relayCodeText;
    [SerializeField] TMP_InputField AvatarUrl;
    [SerializeField] GameObject ServerCam;
    public string NameField;
    private string vivoxChannelName;
    public int avatarIndex = 0;
    [SerializeField] GameObject joiningCanvas;
    [SerializeField] GameObject WarningCanvas;
    [SerializeField] GameObject [] GameObjects;
    public string IP;
    const string NEARBY_WIFI_DEVICES = "android.permission.NEARBY_WIFI_DEVICES";
    const string BLUETOOTH_SCAN = "android.permission.BLUETOOTH_SCAN";
    const string BLUETOOTH_CONNECT = "android.permission.BLUETOOTH_CONNECT";
    async void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        AvatarUrl.text = FindAnyObjectByType<UrlLink>().getUrl();
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            await Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            await VivoxService.Instance.InitializeAsync();
        }
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }
        if (!Permission.HasUserAuthorizedPermission(NEARBY_WIFI_DEVICES))
        {
            Permission.RequestUserPermission(NEARBY_WIFI_DEVICES);
        }
        if (!Permission.HasUserAuthorizedPermission(BLUETOOTH_SCAN))
        {
            Permission.RequestUserPermission(BLUETOOTH_SCAN);
        }
        if (!Permission.HasUserAuthorizedPermission(BLUETOOTH_CONNECT))
        {
            Permission.RequestUserPermission(BLUETOOTH_CONNECT);
        }
        Debug.Log("Vivox SDK Initialized");
        buttonHost.onClick.AddListener(CreateRelayAndStartHost);
        buttonClient.onClick.AddListener(() => JoinRelayAndStartClient(codeInputField.text));
    }

    async void CreateRelayAndStartHost()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(10);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            relayCodeText.text = "Join Code: " + joinCode;
            Debug.Log("Relay created with join code: " + joinCode);
            var relayServerData = new RelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            vivoxChannelName = $"channel_{joinCode}";
            NetworkManager.Singleton.StartServer();

            await LoginVivox();
            await JoinVivoxChannelAsHost(vivoxChannelName);
            ServerCam.SetActive(true);
            gameObject.SetActive(false);
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Failed to create relay: {e.Message}");
            WarningCanvas.SetActive(true);
            WarningCanvas.GetComponentInChildren<TextMeshProUGUI>().text = $"Failed to create session: {e.Message}";
            StartCoroutine(SetcanvasOff());
        }
    }

    async void JoinRelayAndStartClient(string code)
    {
        if (NameField == string.Empty)
        {
            WarningCanvas.SetActive(true);
            WarningCanvas.GetComponentInChildren<TextMeshProUGUI>().text = "Please enter the Name";
            StartCoroutine(SetcanvasOff());
            return;
        }
        if (relayCodeText.text == string.Empty)
        {
            WarningCanvas.SetActive(true);
            WarningCanvas.GetComponentInChildren<TextMeshProUGUI>().text = "Please enter the code";
            StartCoroutine(SetcanvasOff());
            return;
        }
        try
        {
            Debug.Log("Code entered: " + code.ToUpper());
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code.ToUpper());
            var relayServerData = new RelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            vivoxChannelName = $"channel_{code.ToUpper()}";
            NetworkManager.Singleton.StartClient();

            await LoginVivox();
            await JoinVivoxChannelAsClient(vivoxChannelName);
            joiningCanvas.SetActive(true);
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Failed to join relay: {e.Message}");
            WarningCanvas.SetActive(true);
            WarningCanvas.GetComponentInChildren<TextMeshProUGUI>().text = $"Failed to join session: {e.Message}";
            StartCoroutine(SetcanvasOff());
            joiningCanvas.SetActive(false);
        }
    }
    IEnumerator SetcanvasOff()
    {
        yield return new WaitForSeconds(3);
        WarningCanvas.SetActive(false);
    }

    private async System.Threading.Tasks.Task LoginVivox()
    {
        var options = new LoginOptions();
        options.DisplayName = AuthenticationService.Instance.PlayerId;
        options.EnableTTS = false;
        await VivoxService.Instance.LoginAsync(options);
    }

    private async System.Threading.Tasks.Task JoinVivoxChannelAsHost(string channelName)
    {
        await VivoxService.Instance.JoinGroupChannelAsync(
            channelName,
            ChatCapability.TextAndAudio
        );
    }

    private async System.Threading.Tasks.Task JoinVivoxChannelAsClient(string channelName)
    {
        await VivoxService.Instance.JoinGroupChannelAsync(
            channelName,
            ChatCapability.TextAndAudio
        );
    }

    public async System.Threading.Tasks.Task LeaveVivoxChannel()
    {
        if (!string.IsNullOrEmpty(vivoxChannelName))
        {
            await VivoxService.Instance.LeaveChannelAsync(vivoxChannelName);
        }
    }

    public async System.Threading.Tasks.Task LogoutVivox()
    {
        await VivoxService.Instance.LogoutAsync();
    }

    private void OnApplicationQuit()
    {
        _ = LeaveVivoxChannel();
        _ = LogoutVivox();
    }

    public void setName(string name)
    {
        NameField = name;
    }
    public string getVoiceChannelName()
    {
        return vivoxChannelName;
    }
    public void SetIP(string ip)
    {
        IP = ip;
    }
    public void DactivateUi()
    {
        foreach(var Object in GameObjects)
        {
            Object.SetActive(false);
        }
        gameObject.SetActive(false);
    }
}