using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MZBLauncher.WebAuth;

internal sealed class LocalSocksBridge : IDisposable
{
	private readonly string _host;

	private readonly int _port;

	private readonly string _user;

	private readonly string _pass;

	private readonly TcpListener _listener;

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	public LocalSocksBridge(string upstreamHost, int upstreamPort, string user, string pass)
	{
		_host = upstreamHost;
		_port = upstreamPort;
		_user = user ?? "";
		_pass = pass ?? "";
		_listener = new TcpListener(IPAddress.Loopback, 0);
	}

	public int Start()
	{
		_listener.Start();
		int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		AcceptLoopAsync(_cts.Token);
		return port;
	}

	private async Task AcceptLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
				break;
			}
			HandleAsync(client, ct);
		}
	}

	private async Task HandleAsync(TcpClient client, CancellationToken ct)
	{
		TcpClient up = null;
		try
		{
			client.NoDelay = true;
			NetworkStream cs = client.GetStream();
			byte[] array = await ReadExactlyAsync(cs, 2, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (array[0] != 5)
			{
				return;
			}
			await ReadExactlyAsync(cs, array[1], ct).ConfigureAwait(continueOnCapturedContext: false);
			await cs.WriteAsync(new byte[2] { 5, 0 }, ct).ConfigureAwait(continueOnCapturedContext: false);
			up = new TcpClient
			{
				NoDelay = true
			};
			await up.ConnectAsync(_host, _port, ct).ConfigureAwait(continueOnCapturedContext: false);
			NetworkStream us = up.GetStream();
			await us.WriteAsync(new byte[3] { 5, 1, 2 }, ct).ConfigureAwait(continueOnCapturedContext: false);
			if ((await ReadExactlyAsync(us, 2, ct).ConfigureAwait(continueOnCapturedContext: false))[1] == 2)
			{
				byte[] bytes = Encoding.UTF8.GetBytes(_user);
				byte[] bytes2 = Encoding.UTF8.GetBytes(_pass);
				byte[] array2 = new byte[3 + bytes.Length + bytes2.Length];
				array2[0] = 1;
				array2[1] = (byte)bytes.Length;
				Array.Copy(bytes, 0, array2, 2, bytes.Length);
				array2[2 + bytes.Length] = (byte)bytes2.Length;
				Array.Copy(bytes2, 0, array2, 3 + bytes.Length, bytes2.Length);
				await us.WriteAsync(array2, ct).ConfigureAwait(continueOnCapturedContext: false);
				if ((await ReadExactlyAsync(us, 2, ct).ConfigureAwait(continueOnCapturedContext: false))[1] == 0)
				{
					Task task = cs.CopyToAsync(us, 81920, ct);
					Task task2 = us.CopyToAsync(cs, 81920, ct);
					await Task.WhenAny(task, task2).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch
		{
		}
		finally
		{
			try
			{
				up?.Dispose();
			}
			catch
			{
			}
			try
			{
				client.Dispose();
			}
			catch
			{
			}
		}
	}

	private static async Task<byte[]> ReadExactlyAsync(NetworkStream s, int count, CancellationToken ct)
	{
		byte[] buf = new byte[count];
		int num;
		for (int read = 0; read < count; read += num)
		{
			num = await s.ReadAsync(buf.AsMemory(read, count - read), ct).ConfigureAwait(continueOnCapturedContext: false);
			if (num == 0)
			{
				throw new EndOfStreamException();
			}
		}
		return buf;
	}

	public void Dispose()
	{
		try
		{
			_cts.Cancel();
		}
		catch
		{
		}
		try
		{
			_listener.Stop();
		}
		catch
		{
		}
		_cts.Dispose();
	}
}
