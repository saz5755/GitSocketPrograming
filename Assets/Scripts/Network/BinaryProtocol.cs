using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 고빈도 UDP 패킷 바이너리 인코딩 (v4.3 N-1) — 서버 SocketServer/BinaryProtocol.cs 와 같은 포맷.
///   [0]=Magic(0xB7) [1]=PacketType  이후 필드 (리틀 엔디언)
///   MOVE  C→S : tick i32, pos 3×f32, rot 3×u16, flags u8, anim u8, [sendMs u32, fx u8]  = 31B (JSON ~190B)
///   MOVE  S→C : tick i32, nick str, pos, rot, flags, anim, [sendMs u32, fx u8]
///   AI_MOVE_BATCH (v4.4) : sendMs u32, count u8, count × { nick str, pos, rot, speed f32, flags u8, fx u8 }
///     호스트가 한 틱의 AI 전체를 한 패킷으로 — 편대 전원이 같은 송신 시각을 공유해 게스트 화면에서 함께 움직인다
///   [ ] 는 v4.4 꼬리 필드 — 구버전은 읽지 않고 무시한다 (하위 호환)
///   fx : bit0 스모크 ON, bit1~3 스모크 색 인덱스 (AirshowSmokeFX.Palette)
///   MOVE_ACK  : tick i32, pos, rot
///   AI_MOVE   : nick str, pos, rot, speed f32, flags u8
///   MISSILE_MOVE : id str, pos, rot, speed f32
/// str = 길이 u8 + UTF-8. 회전은 0..360° 를 u16 으로 양자화 (0.0055°).
/// 송신은 호출자 버퍼에 직접 써서 할당이 없다 (기존 JSON 직렬화가 클라 GC 의 대부분이었다).
/// </summary>
public static class BinaryProtocol
{
    public const byte Magic = 0xB7;

    const byte FlagMove = 1, FlagFlying = 2, FlagBoarded = 4;

    public static bool IsBinary(byte[] data, int len) => len >= 2 && data[0] == Magic;
    public static PacketType TypeOf(byte[] data) => (PacketType)data[1];

    // ── 인코딩 (C→S) — buf 에 쓰고 길이 반환 ───────────────────────────────
    public static int WriteMove(byte[] buf, int tick, float px, float py, float pz, float rx, float ry, float rz,
                                bool isMove, bool isFlying, bool boarded, int anim, uint sendMs = 0, byte fx = 0)
    {
        int n = Header(buf, PacketType.MOVE);
        n = I32(buf, n, tick);
        n = Pose(buf, n, px, py, pz, rx, ry, rz);
        buf[n++] = (byte)((isMove ? FlagMove : 0) | (isFlying ? FlagFlying : 0) | (boarded ? FlagBoarded : 0));
        buf[n++] = (byte)anim;
        n = I32(buf, n, (int)sendMs);
        buf[n++] = fx;
        return n;
    }

    // ── AI 배치 (호스트) — Begin 으로 헤더를 쓰고 Append 를 반복, count 는 buf[6] ─────
    public const int AIBatchCountIndex = 6;

    public static int BeginAIBatch(byte[] buf, uint sendMs)
    {
        int n = Header(buf, PacketType.AI_MOVE_BATCH);
        n = I32(buf, n, (int)sendMs);
        buf[n++] = 0;   // count
        return n;
    }

    public static int AppendAIBatch(byte[] buf, int n, string nick, float px, float py, float pz,
                                    float rx, float ry, float rz, float speed, byte fx)
    {
        n = Str(buf, n, nick);
        n = Pose(buf, n, px, py, pz, rx, ry, rz);
        n = F32(buf, n, speed);
        buf[n++] = speed > 0.5f ? FlagMove : (byte)0;
        buf[n++] = fx;
        buf[AIBatchCountIndex]++;
        return n;
    }

    /// <summary>배치를 개별 AIMovePacket 으로 풀어 out 에 추가. 송신 시각은 공통.</summary>
    public static bool ReadAIBatch(byte[] d, int len, List<AIMovePacket> outList, out uint sendMs)
    {
        var r = new Reader(d, len);
        sendMs = (uint)r.I32();
        int count = r.U8();
        for (int i = 0; i < count && r.Ok; i++)
        {
            var p = new AIMovePacket { type = PacketType.AI_MOVE, nickname = r.Str() };
            r.Pose(out p.posX, out p.posY, out p.posZ, out p.rotX, out p.rotY, out p.rotZ);
            p.speed  = r.F32();
            p.isMove = (r.U8() & FlagMove) != 0;
            p.fx     = r.U8();
            p.sendMs = sendMs;
            if (r.Ok) outList.Add(p);
        }
        return r.Ok;
    }

    public static int WriteAIMove(byte[] buf, string nick, float px, float py, float pz, float rx, float ry, float rz, float speed)
    {
        int n = Header(buf, PacketType.AI_MOVE);
        n = Str(buf, n, nick);
        n = Pose(buf, n, px, py, pz, rx, ry, rz);
        n = F32(buf, n, speed);
        buf[n++] = speed > 0.5f ? FlagMove : (byte)0;
        return n;
    }

    public static int WriteMissileMove(byte[] buf, string id, float px, float py, float pz, float rx, float ry, float rz, float speed)
    {
        int n = Header(buf, PacketType.MISSILE_MOVE);
        n = Str(buf, n, id);
        n = Pose(buf, n, px, py, pz, rx, ry, rz);
        return F32(buf, n, speed);
    }

    // ── 디코딩 (S→C) ────────────────────────────────────────────────────────
    public static MoveBroadcastPacket ReadMove(byte[] d, int len)
    {
        var r = new Reader(d, len);
        var p = new MoveBroadcastPacket { type = PacketType.MOVE, tick = r.I32(), nickname = r.Str() };
        r.Pose(out p.posX, out p.posY, out p.posZ, out p.rotX, out p.rotY, out p.rotZ);
        byte f = r.U8();
        p.isMove = (f & FlagMove) != 0; p.isFlying = (f & FlagFlying) != 0; p.isBoardedInCockpit = (f & FlagBoarded) != 0;
        p.animState = r.U8();
        if (!r.Ok) return null;
        if (r.Remaining >= 5) { p.sendMs = (uint)r.I32(); p.fx = r.U8(); }   // v4.4 꼬리 필드
        return p;
    }

    public static MoveAckPacket ReadMoveAck(byte[] d, int len)
    {
        var r = new Reader(d, len);
        var p = new MoveAckPacket { type = PacketType.MOVE_ACK, tick = r.I32() };
        r.Pose(out p.posX, out p.posY, out p.posZ, out p.rotX, out p.rotY, out p.rotZ);
        return r.Ok ? p : null;
    }

    public static AIMovePacket ReadAIMove(byte[] d, int len)
    {
        var r = new Reader(d, len);
        var p = new AIMovePacket { type = PacketType.AI_MOVE, nickname = r.Str() };
        r.Pose(out p.posX, out p.posY, out p.posZ, out p.rotX, out p.rotY, out p.rotZ);
        p.speed  = r.F32();
        p.isMove = (r.U8() & FlagMove) != 0;
        return r.Ok ? p : null;
    }

    public static MissileMovePacket ReadMissileMove(byte[] d, int len)
    {
        var r = new Reader(d, len);
        var p = new MissileMovePacket { type = PacketType.MISSILE_MOVE, missileId = r.Str() };
        r.Pose(out p.posX, out p.posY, out p.posZ, out p.rotX, out p.rotY, out p.rotZ);
        p.speed = r.F32();
        return r.Ok ? p : null;
    }

    // ── 저수준 ──────────────────────────────────────────────────────────────
    static ushort QAngle(float deg) => (ushort)Math.Round(((deg % 360f) + 360f) % 360f / 360f * 65535f);
    static float  DAngle(ushort q)  => q / 65535f * 360f;

    static int Header(byte[] b, PacketType t) { b[0] = Magic; b[1] = (byte)t; return 2; }

    static int I32(byte[] b, int n, int v)
    {
        b[n] = (byte)v; b[n + 1] = (byte)(v >> 8); b[n + 2] = (byte)(v >> 16); b[n + 3] = (byte)(v >> 24);
        return n + 4;
    }

    static int F32(byte[] b, int n, float v) => I32(b, n, BitConverter.SingleToInt32Bits(v));

    static int U16(byte[] b, int n, ushort v) { b[n] = (byte)v; b[n + 1] = (byte)(v >> 8); return n + 2; }

    static int Str(byte[] b, int n, string s)
    {
        s ??= "";
        int len = Encoding.UTF8.GetBytes(s, 0, Math.Min(s.Length, 64), b, n + 1);
        b[n] = (byte)len;
        return n + 1 + len;
    }

    static int Pose(byte[] b, int n, float px, float py, float pz, float rx, float ry, float rz)
    {
        n = F32(b, n, px); n = F32(b, n, py); n = F32(b, n, pz);
        n = U16(b, n, QAngle(rx)); n = U16(b, n, QAngle(ry)); return U16(b, n, QAngle(rz));
    }

    struct Reader
    {
        readonly byte[] _b; readonly int _len; int _n;
        public bool Ok;
        public Reader(byte[] b, int len) { _b = b; _len = len; _n = 2; Ok = true; }
        bool Has(int k) { if (_n + k <= _len) return true; Ok = false; return false; }
        public int Remaining => _len - _n;
        public byte   U8()  { if (!Has(1)) return 0; return _b[_n++]; }
        public ushort U16() { if (!Has(2)) return 0; var v = BitConverter.ToUInt16(_b, _n); _n += 2; return v; }
        public int    I32() { if (!Has(4)) return 0; var v = BitConverter.ToInt32(_b, _n); _n += 4; return v; }
        public float  F32() { if (!Has(4)) return 0; var v = BitConverter.ToSingle(_b, _n); _n += 4; return v; }
        public string Str()
        {
            int len = U8();
            if (!Has(len)) return "";
            var s = Encoding.UTF8.GetString(_b, _n, len); _n += len; return s;
        }
        public void Pose(out float px, out float py, out float pz, out float rx, out float ry, out float rz)
        {
            px = F32(); py = F32(); pz = F32();
            rx = DAngle(U16()); ry = DAngle(U16()); rz = DAngle(U16());
        }
    }
}
