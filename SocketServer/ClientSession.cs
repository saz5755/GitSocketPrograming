using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

public class ClientSession
{
    public TcpClient client = null!;
    public IPEndPoint? udpEndPoint;

    public NetworkStream stream = null!;

    public bool isLogin;

    public Player? player;

    public readonly object sendLock = new();

    /// <summary>로그인 시 발급 — UDP_CONNECT 에 실려 와야 UDP 엔드포인트를 등록한다 (닉네임 위장 방지, v4.3 N-2)</summary>
    public string sessionToken = string.Empty;

    /// <summary>이 클라이언트가 바이너리 UDP 를 보냈다 → 응답·브로드캐스트도 바이너리로 (v4.3 N-1)</summary>
    public volatile bool binaryUdp;

    // ── 비동기 송신 큐 (v4.3 N-3) ────────────────────────────────────────────
    // 기존: 세션 락 안에서 동기 Write — 느린 클라이언트 한 명이 방 브로드캐스트·틱 스레드를 붙잡았다.
    // 이제 송신은 프레임을 큐에 넣고 바로 돌아오며, 세션별 작업이 순서대로 비동기 전송한다.
    // 큐가 넘치면(클라이언트가 수신을 못 따라옴) 그 세션만 끊는다.
    const int MaxQueuedFrames = 2048;
    readonly Channel<byte[]> _out = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(MaxQueuedFrames) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    int _queued;
    volatile bool _closed;

    public void StartWriter() => _ = Task.Run(WriterLoop);

    public void Enqueue(byte[] frame)
    {
        if (_closed) return;
        if (Interlocked.Increment(ref _queued) > MaxQueuedFrames || !_out.Writer.TryWrite(frame))
        {
            Console.WriteLine($"[TCP] Send queue overflow — dropping slow client {player?.nickname}");
            Close();
        }
    }

    async Task WriterLoop()
    {
        try
        {
            await foreach (var frame in _out.Reader.ReadAllAsync())
            {
                Interlocked.Decrement(ref _queued);
                await stream.WriteAsync(frame);
            }
        }
        catch (Exception e)
        {
            if (!_closed) Console.WriteLine($"[TCP] Send error [{player?.nickname}]: {e.Message}");
            Close();
        }
    }

    /// <summary>소켓을 닫는다 — 수신 루프가 예외로 빠져나가며 정상 퇴장 처리(DisconnectSession)를 한다.</summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _out.Writer.TryComplete();
        try { stream?.Close(); } catch { }
        try { client?.Close(); } catch { }
    }
}
