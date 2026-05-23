using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HTTP_Client;
using UnityEngine;

public class WebSocketClient : MonoBehaviour
{
	private ClientWebSocket webSocket;

	private MessageHandler _messageHandler = new MessageHandler();

	private string serverUrl = "ws://localhost:8080/ws/game";

	[SerializeField]
	private float reconnectDelaySeconds = 3f;

	private bool _isQuitting;

	private CancellationTokenSource _lifetimeCts;

	private CancellationTokenSource _connectionCts;

	private bool _isApplicationQuitting;

	private async void Awake()
	{
		_lifetimeCts = new CancellationTokenSource();
	}

	public async Task ConnectToServer(string gameId)
	{
		await DisconnectAsync();
		if (!_isApplicationQuitting)
		{
			_connectionCts = new CancellationTokenSource();
			ConnectLoop(gameId, _connectionCts.Token);
		}
	}

	private async Task ConnectLoop(string gameId, CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			SafeDisposeSocket();
			ClientWebSocket socket = (webSocket = new ClientWebSocket());
			try
			{
				Debug.Log("Attempting to connect to WebSocket server...");
				await webSocket.ConnectAsync(new Uri(serverUrl + "?gameId=" + gameId), token);
				Debug.Log("Connected to WebSocket server successfully!");
				await ReceiveMessages(socket, token);
			}
			catch (OperationCanceledException)
			{
				Debug.Log("WebSocket connection loop canceled intentionally.");
				break;
			}
			catch (Exception ex2)
			{
				string text = ((ex2.InnerException != null) ? (" | Inner: " + ex2.InnerException.Message) : string.Empty);
				Debug.LogError("WebSocket connection error: " + ex2.Message + text);
			}
			finally
			{
				if (webSocket == socket)
				{
					SafeDisposeSocket();
				}
			}
			if (token.IsCancellationRequested)
			{
				break;
			}
			try
			{
				Debug.Log($"Waiting {reconnectDelaySeconds} seconds before attempting reconnect...");
				await Task.Delay(TimeSpan.FromSeconds(reconnectDelaySeconds), token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
		Debug.Log("ConnectLoop background instance has safely terminated.");
	}

	public async void SendMessageToServer(string gameId, string message)
	{
		ClientWebSocket clientWebSocket = webSocket;
		if (clientWebSocket == null || clientWebSocket.State != WebSocketState.Open)
		{
			Debug.LogWarning("WebSocket send skipped because socket is not connected.");
			return;
		}
		try
		{
			byte[] bytes = Encoding.UTF8.GetBytes(message);
			await clientWebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, _lifetimeCts.Token);
			Debug.Log("Sent: " + message);
		}
		catch (Exception ex)
		{
			Debug.LogError("WebSocket send failed: " + ex.Message);
		}
	}

	private async Task ReceiveMessages(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[16384];
		while (!_isQuitting && socket != null && socket.State == WebSocketState.Open)
		{
			WebSocketReceiveResult webSocketReceiveResult;
			try
			{
				webSocketReceiveResult = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			catch (WebSocketException ex3)
			{
				Debug.LogWarning("WebSocket receive interrupted: " + ex3.Message);
				return;
			}
			if (webSocketReceiveResult.MessageType == WebSocketMessageType.Close)
			{
				Debug.LogWarning("WebSocket closed by server.");
				return;
			}
			string @string = Encoding.UTF8.GetString(buffer, 0, webSocketReceiveResult.Count);
			Debug.Log("Received from server: " + @string);
			try
			{
				_messageHandler.HandleMessage(@string);
			}
			catch (Exception ex4)
			{
				Debug.LogError("WebSocket message handling failed: " + ex4.Message);
			}
		}
		Debug.Log("ReceiveMessages loop ended.");
	}

	private async void OnApplicationQuit()
	{
		_isQuitting = true;
		_lifetimeCts?.Cancel();
		if (webSocket != null && webSocket.State == WebSocketState.Open)
		{
			await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
		}
		SafeDisposeSocket();
		_lifetimeCts?.Dispose();
		Debug.Log("WebSocket connection closed.");
	}

	private void SafeDisposeSocket()
	{
		if (webSocket == null)
		{
			return;
		}
		try
		{
			webSocket.Dispose();
		}
		catch (Exception ex)
		{
			Debug.LogWarning("WebSocket dispose warning: " + ex.Message);
		}
		finally
		{
			webSocket = null;
		}
	}

	public MessageHandler GetMessageHandler()
	{
		return _messageHandler;
	}

	public async Task DisconnectAsync()
	{
		if (_connectionCts != null)
		{
			_connectionCts.Cancel();
			_connectionCts.Dispose();
			_connectionCts = null;
		}
		if (webSocket != null)
		{
			try
			{
				if (webSocket.State == WebSocketState.Open)
				{
					await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client resetting connection", CancellationToken.None);
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				SafeDisposeSocket();
			}
		}
		_messageHandler.Reset();
	}
}
