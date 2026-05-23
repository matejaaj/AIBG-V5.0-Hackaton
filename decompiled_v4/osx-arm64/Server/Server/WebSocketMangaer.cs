using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SharedLibrary;

namespace Server;

public class WebSocketMangaer
{
	private static readonly ConcurrentDictionary<string, WebSocket> _sockets = new ConcurrentDictionary<string, WebSocket>();

	public void AddSocket(string gameId, WebSocket socket)
	{
		_sockets[gameId] = socket;
	}

	public static void RemoveSocket(string gameId)
	{
		_sockets.TryRemove(gameId, out WebSocket _);
	}

	public async Task BroadcastMessage(string message)
	{
		byte[] messageBytes = Encoding.UTF8.GetBytes(message);
		foreach (KeyValuePair<string, WebSocket> socket in _sockets)
		{
			if (socket.Value.State == WebSocketState.Open)
			{
				await socket.Value.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
			}
		}
	}

	public static WebSocket? GetSocket(string gameId)
	{
		_sockets.TryGetValue(gameId, out WebSocket value);
		return value;
	}

	public static async Task NotifyClients(string gameId, Message message)
	{
		if (_sockets.TryGetValue(gameId, out WebSocket value))
		{
			string text = JsonConvert.SerializeObject(message);
			Console.WriteLine(text);
			byte[] bytes = Encoding.UTF8.GetBytes(text);
			if (value.State == WebSocketState.Open)
			{
				await value.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
				Console.WriteLine("Message Sent");
			}
			else
			{
				RemoveSocket(gameId);
			}
		}
	}
}
