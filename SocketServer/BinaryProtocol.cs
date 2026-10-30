using System.Text;

/// <summary>
/// 고빈도 UDP 패킷 바이너리 인코딩 (v4.3 N-1) — 클라이언트 Assets/Scripts/Network/BinaryProtocol.cs 와 같은 포맷.
///   [0]=Magic(0xB7) [1]=PacketType  이후 필드 (리틀 엔디언)
///   MOVE  C→S : tick i32, pos 3×f32, rot 3×u16, flags u8, anim u8, [sendMs u32, fx u8]  = 31B (JSON ~190B)
///   MOVE  S→C : tick i32, nick str, pos, rot, flags, anim, sendMs u32, fx u8
///   AI_MOVE_BATCH (v4.4) : sendMs u32, count u8, count × { nick str, pos, rot, speed f32, flags u8, fx u8 }
///     서버는 검증·풀 갱신만 하고 바이트를 그대로 중계한다 (재인코딩 없음)
///   [ ] 는 v4.4 꼬리 필드 — 없으면 0, 구버전 클라이언트는 뒤에 붙은 바이트를 무시한다
///   MOVE_ACK  : tick i32, pos, rot
///   AI_MOVE   : nick str, pos, rot, speed f32, flags u8
///   MISSILE_MOVE : id str, pos, rot, speed f32
/// str = 길이 u8 + UTF-8. 회전은 0..360° 를 u16 으로 양자화 (0.0055°).
/// JSON 첫 바이트는 '{' 이므로 Magic 으로 구분한다 — JSON 클라이언트(테스트 봇 등)도 계속 받는다.
/// </summary>
public static class BinaryProtocol
{
    public const byte Magic = 0xB7;

    public static bool IsBinary(byte[] data) => data.Length >= 2 && data[0] == Magic;
    public static PacketType TypeOf(byte[] data) => (PacketType)data[1];

    const byte FlagMove = 1, FlagFlying = 2, FlagBoarded = 4;

    // ── 인코딩 ──────────────────────────────────────────────────────────────
    public static byte[] EncodeMoveBroadcast(MoveBroadcastPacket p)
    {
        var w = new Writer(64, PacketType.MOVE);
        w.I32(p.tick); w.Str(p.nickname);
        w.Pose(p.posX, p.posY, p.posZ, p.rotX, p.rotY, p.rotZ);
        w.U8((byte)((p.isMove ? FlagMove : 0) | (p.isFlying ? FlagFlying : 0) | (p.isBoardedInCockpit ? FlagBoarded : 0)));
        w.U8((byte)p.animState);
        w.I32((int)p.sendMs);
        w.U8(p.fx);
        return w.ToArray();
    }

    public static byte[] EncodeMoveAck(MoveAckPacket p)
    {
        var w = new Writer(32, PacketType.MOVE_ACK);
        w.I32(p.tick);
        w.Pose(p.posX, p.posY, p.posZ, p.rotX, p.rotY, p.rotZ);
        return w.ToArray();
    }

    public static byte[] EncodeAIMove(AIMovePacket p)
    {
        var w = new Writer(64, PacketType.AI_MOVE);
        w.Str(p.nickname);
        w.Pose(p.posX, p.posY, p.posZ, p.rotX, p.rotY, p.rotZ);
        w.F32(p.speed);
        w.U8(p.isMove ? FlagMove : (byte)0);
        return w.ToArray();
    }

    public static byte[] EncodeMissileMove(MissileMovePacket p)
    {
        var w = new Writer(48, PacketType.MISSILE_MOVE);
        w.Str(p.missileId);
        w.Pose(p.posX, p.posY, p.posZ, p.rotX, p.rotY, p.rotZ);
        w.F32(p.speed);
        return w.ToArray();
    }

    // ── 디코딩 (C→S) ────────────────────────────────────────────────────────
    public static MovePacket? DecodeMove(byte[] d)
    {
        var r = new Reader(d);
        var p = new MovePacket { type = PacketType.MOVE, tick = r.I32() };
        r.Pose(out p.posX, out p.posY, out p.posZ, out p.rotX, out p.rotY, out p.rotZ);
        byte f = r.U8();
        p.isMove = (f & FlagMove) != 0; p.isFlying = (f & FlagFlying) != 0; p.isBoardedInCockpit = (f & FlagBoarded) != 0;
        p.animState = r.U8();
        if (!r.Ok) return null;
        if (r.Remaining >= 5) { p.sendMs = (uint)r.I32(); p.fx = r.U8(); }
        return p;
    }

    public const int MaxBatchEntries = 32;

    /// <summary>AI 배치 검증용 해석 — 항목을 out 에 채운다 (풀 갱신·피격 검증 위치 기록용)</summary>
    public static bool DecodeAIBatch(byte[] d, List<AIMovePacket> outList)
    {
        var r = new Reader(d);
        r.I32();                       // sendMs — 서버는 쓰지 않음 (그대로 중계)
        int count = r.U8();
        if (count > MaxBatchEntries) return false;
        for (int i = 0; i < count; i++)
        {
            var p = new AIMovePacket { type = PacketType.AI_MOVE, nickname = r.Str() };
            r.Pose(out p.posX, out p.posY, out p.posZ, out p.rotX, out p.rotY, out p.rotZ);
            p.speed  = r.F32();
            p.isMove = (r.U8() & FlagMove) != 0;
            r.U8();                    // fx
            if (!r.Ok) return false;
            outList.Add(p);
        }
        return r.Ok && r.Remaining == 0;
    }

    public static AIMovePacket? DecodeAIMove(byte[] d)
    {
        var r = new Reader(d);
        var p = new AIMovePacket { type = PacketType.AI_MOVE, nickname = r.Str() };
        r.Pose(out p.posX, out p.posY, out p.posZ, out p.rotX, out p.rotY, out p.rotZ);
        p.speed  = r.F32();
        p.isMove = (r.U8() & FlagMove) != 0;
        return r.Ok ? p : null;
    }

    public static MissileMovePacket? DecodeMissileMove(byte[] d)
    {
        var r = new Reader(d);
        var p = new MissileMovePacket { type = PacketType.MISSILE_MOVE, missileId = r.Str() };
        r.Pose(out p.posX, out p.posY, out p.posZ, out p.rotX, out p.rotY, out p.rotZ);
        p.speed = r.F32();
        return r.Ok ? p : null;
    }

    // ── 저수준 ──────────────────────────────────────────────────────────────
    static ushort QAngle(float deg) => (ushort)Math.Round(((deg % 360f) + 360f) % 360f / 360f * 65535f);
    static float  DAngle(ushort q)  => q / 65535f * 360f;

    struct Writer
    {
        byte[] _b; int _n;
        public Writer(int cap, PacketType type) { _b = new byte[cap]; _n = 0; U8(Magic); U8((byte)type); }
        void Need(int k) { if (_n + k > _b.Length) Array.Resize(ref _b, Math.Max(_b.Length * 2, _n + k)); }
        public void U8(byte v)  { Need(1); _b[_n++] = v; }
        public void U16(ushort v) { Need(2); BitConverter.TryWriteBytes(_b.AsSpan(_n), v); _n += 2; }
        public void I32(int v)  { Need(4); BitConverter.TryWriteBytes(_b.AsSpan(_n), v); _n += 4; }
        public void F32(float v) { Need(4); BitConverter.TryWriteBytes(_b.AsSpan(_n), v); _n += 4; }
        public void Str(string? s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            int len = Math.Min(bytes.Length, 255);
            Need(1 + len); _b[_n++] = (byte)len;
            Buffer.BlockCopy(bytes, 0, _b, _n, len); _n += len;
        }
        public void Pose(float px, float py, float pz, float rx, float ry, float rz)
        { F32(px); F32(py); F32(pz); U16(QAngle(rx)); U16(QAngle(ry)); U16(QAngle(rz)); }
        public byte[] ToArray() { var a = new byte[_n]; Buffer.BlockCopy(_b, 0, a, 0, _n); return a; }
    }

    struct Reader
    {
        readonly byte[] _b; int _n;
        public bool Ok;
        public Reader(byte[] b) { _b = b; _n = 2; Ok = true; }
        bool Has(int k) { if (_n + k <= _b.Length) return true; Ok = false; return false; }
        public int Remaining => _b.Length - _n;
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
