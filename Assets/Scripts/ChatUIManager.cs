using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatUIManager : MonoBehaviour
{
    [Header("Chat")]
    public TMP_Text       chatLogText;
    public TMP_InputField inputField;
    public Button         sendButton;

    void Start()
    {
        if (sendButton  != null) sendButton.onClick.AddListener(OnClickSend);
        if (inputField  != null)
        {
            inputField.onSubmit.AddListener(_ => OnClickSend());
            inputField.interactable = false;
        }

        SocketClient sc = NetworkManager.Instance?.socketClient;
        if (sc != null)
        {
            sc.OnChat        += AddMessage;
            sc.OnLoginResult += HandleLoginResult;
        }
    }

    void OnDestroy()
    {
        SocketClient sc = NetworkManager.Instance?.socketClient;
        if (sc != null)
        {
            sc.OnChat        -= AddMessage;
            sc.OnLoginResult -= HandleLoginResult;
        }
    }

    void HandleLoginResult(LoginResultPacket packet)
    {
        if (inputField != null) inputField.interactable = packet.success;
    }

    void OnClickSend()
    {
        if (string.IsNullOrEmpty(inputField?.text)) return;
        NetworkManager.Instance?.socketClient.SendChat(inputField.text);
        inputField.text = "";
    }

    public void AddMessage(string message)
    {
        if (chatLogText != null)
            chatLogText.text += message + "\n";
    }
}
